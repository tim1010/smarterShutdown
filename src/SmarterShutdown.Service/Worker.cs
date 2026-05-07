using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmarterShutdown.Core.IPC;
using SmarterShutdown.Core.Models;
using SmarterShutdown.Core.Policy;
using SmarterShutdown.Core.Power;
using SmarterShutdown.Core.Scheduler;
using SmarterShutdown.Core.Shutdown;

namespace SmarterShutdown.Service;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan PolicyRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleTick = TimeSpan.FromSeconds(30);

    private readonly ILogger<Worker> _logger;
    private readonly PolicyReader _policyReader;
    private readonly SchedulerEngine _scheduler;
    private readonly IShutdownExecutor _executor;
    private readonly IBatteryStatus _battery;
    private readonly IPipeServer _pipe;
    private readonly TimeProvider _time;

    private PolicySettings _policy = new();
    private DateTimeOffset _lastPolicyRead = DateTimeOffset.MinValue;
    private DateTime? _nextAction;
    private DateTime? _warningBroadcastFor;
    private readonly PostponeTracker _postpones = new();
    private readonly TeamsDeferralPolicy _teamsDeferral = new();

    // Most recent client status reported by the Notifier (over the pipe).
    private bool _teamsCallActive;
    private int _idleMinutes;

    public Worker(
        ILogger<Worker> logger,
        PolicyReader policyReader,
        SchedulerEngine scheduler,
        IShutdownExecutor executor,
        IBatteryStatus battery,
        IPipeServer pipe,
        TimeProvider time)
    {
        _logger = logger;
        _policyReader = policyReader;
        _scheduler = scheduler;
        _executor = executor;
        _battery = battery;
        _pipe = pipe;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _pipe.Start(stoppingToken);

        RefreshPolicy();
        RecomputeNextAction();
        _logger.LogInformation(EventIds.ServiceStarted,
            "SmarterShutdown service started. Next action: {NextAction}", FormatNext());
        WarnIfNoActiveDays();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainIncomingAsync(stoppingToken);
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Worker loop");
                await SafeDelay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task DrainIncomingAsync(CancellationToken ct)
    {
        while (_pipe.Incoming.TryRead(out var msg))
        {
            await HandleAsync(msg, ct);
        }
    }

    private async Task HandleAsync(PipeMessage msg, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case MessageType.PostponeRequest:
                await HandlePostponeRequestAsync(ct);
                break;

            case MessageType.SuspendRequest:
                await HandleSuspendRequestAsync(msg, ct);
                break;

            case MessageType.TeamsCallStatus:
                if (msg.TeamsCallActive is bool active) _teamsCallActive = active;
                if (msg.IdleMinutes is int idle) _idleMinutes = idle;
                break;

            default:
                _logger.LogDebug("Ignoring incoming message of type {Type}", msg.Type);
                break;
        }
    }

    private async Task HandlePostponeRequestAsync(CancellationToken ct)
    {
        if (!_policy.AllowPostpone || _nextAction is null) return;

        if (!_postpones.TryPostpone(_policy.MaxPostpones))
        {
            _logger.LogDebug("PostponeRequest denied — max postpones reached ({Count}/{Max})",
                _postpones.Count, _policy.MaxPostpones);
            return;
        }

        _nextAction = _nextAction.Value.AddMinutes(_policy.PostponeMinutes);
        _warningBroadcastFor = null;
        _teamsDeferral.Reset();

        _logger.LogInformation(EventIds.ActionPostponed,
            "Action postponed by user, rescheduled to {NextAction}", FormatNext());

        await _pipe.BroadcastAsync(
            new PipeMessage
            {
                Type = MessageType.PostponeAck,
                ScheduledAt = _nextAction,
                PostponeAllowed = _policy.AllowPostpone,
                PostponesRemaining = _policy.MaxPostpones == 0
                    ? null
                    : Math.Max(0, _policy.MaxPostpones - _postpones.Count),
                MaxPostpones = _policy.MaxPostpones == 0 ? null : _policy.MaxPostpones,
            },
            ct);
    }

    private async Task HandleSuspendRequestAsync(PipeMessage request, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(request.UserName) ? "(unknown)" : request.UserName!;
        _logger.LogWarning(EventIds.SuspendedByAdmin,
            "Action suspended by local admin {UserName}", user);

        AdvancePastCurrentSlot();

        await _pipe.BroadcastAsync(
            new PipeMessage
            {
                Type = MessageType.SuspendAck,
                ScheduledAt = _nextAction,
                UserName = user,
            },
            ct);
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        if (DueForPolicyRefresh())
        {
            RefreshPolicy();
            RecomputeNextAction();
            _postpones.Reset();
            _warningBroadcastFor = null;
            _teamsDeferral.Reset();
            _logger.LogInformation(EventIds.PolicyRefreshed,
                "Policy refreshed. Next action: {NextAction}", FormatNext());
            WarnIfNoActiveDays();
            await _pipe.BroadcastAsync(new PipeMessage { Type = MessageType.PolicyRefreshed }, stoppingToken);
        }

        var now = _time.GetLocalNow().LocalDateTime;

        if (_nextAction is null)
        {
            await SafeDelay(IdleTick, stoppingToken);
            return;
        }

        // Idle-aware warning lead time: when the user is idle, use the shortened window
        // (IdleWarningMinutes) so the device doesn't sit idle waiting through a long popup
        // nobody is watching.
        var isIdle = IsClientIdle();
        var effectiveLead = isIdle ? _policy.IdleWarningMinutes : _policy.WarningMinutes;
        var warningOpensAt = _nextAction.Value.AddMinutes(-effectiveLead);

        if (now >= warningOpensAt && _warningBroadcastFor != _nextAction)
        {
            _warningBroadcastFor = _nextAction;
            await _pipe.BroadcastAsync(
                new PipeMessage
                {
                    Type = isIdle ? MessageType.ShutdownPendingIdle : MessageType.ShutdownPending,
                    ScheduledAt = _nextAction,
                    PostponeAllowed = _policy.AllowPostpone && !isIdle,
                    PostponesRemaining = _policy.AllowPostpone && !isIdle && _policy.MaxPostpones > 0
                        ? Math.Max(0, _policy.MaxPostpones - _postpones.Count)
                        : null,
                    MaxPostpones = _policy.MaxPostpones == 0 ? null : _policy.MaxPostpones,
                    Message = string.IsNullOrEmpty(_policy.WarningMessage) ? null : _policy.WarningMessage,
                    IdleMinutes = isIdle ? _idleMinutes : null,
                },
                stoppingToken);

            // Capture both pieces of info that turned out to be hard to determine after the
            // fact: whether the active or idle path fired, and how many Notifiers were
            // listening. Zero clients means nobody saw the popup — useful when investigating
            // "machine rebooted but I never saw a warning" reports.
            _logger.LogInformation(EventIds.WarningBroadcast,
                "Warning broadcast ({Mode}, lead={LeadMinutes}min) sent to {ClientCount} connected client(s); idle={IdleMinutes}min",
                isIdle ? "Idle" : "Active",
                effectiveLead,
                _pipe.ConnectedClients,
                _idleMinutes);
        }

        if (now < _nextAction.Value)
        {
            var sleep = MinSpan(_nextAction.Value - now, IdleTick);
            await SafeDelay(sleep, stoppingToken);
            return;
        }

        // Action time has arrived.
        if (_policy.SkipIfOnBattery && _battery.IsOnBattery())
        {
            _logger.LogWarning(EventIds.SkippedOnBattery, "Action skipped — device on battery");
            AdvancePastCurrentSlot();
            return;
        }

        if (_policy.TeamsDefer)
        {
            var decision = _teamsDeferral.Evaluate(now, _teamsCallActive, _policy.TeamsDeferMaxMinutes);
            switch (decision)
            {
                case TeamsDeferralDecision.DeferStarting:
                    _logger.LogInformation(EventIds.ActionDeferredTeams,
                        "Action deferred — Teams call in progress");
                    await _pipe.BroadcastAsync(
                        new PipeMessage
                        {
                            Type = MessageType.DeferredTeams,
                            ScheduledAt = _nextAction,
                            Reason = "Teams call in progress",
                        },
                        stoppingToken);
                    await SafeDelay(IdleTick, stoppingToken);
                    return;

                case TeamsDeferralDecision.Defer:
                    await SafeDelay(IdleTick, stoppingToken);
                    return;

                case TeamsDeferralDecision.MaxReached:
                    _logger.LogWarning(EventIds.TeamsDeferLimitReached,
                        "Teams deferral limit reached, proceeding with action");
                    _teamsDeferral.Reset();
                    break;

                case TeamsDeferralDecision.Proceed:
                    // No active call — fall through and fire.
                    break;
            }
        }

        try
        {
            _logger.LogInformation(EventIds.ActionExecuted,
                "Executing {Action} (force={Force})", _policy.Action, _policy.ForceShutdown);
            _executor.Execute(_policy.Action, _policy.ForceShutdown);
        }
        catch (Exception ex)
        {
            _logger.LogError(EventIds.ShutdownFailed, ex,
                "Shutdown execution failed: {Message}", ex.Message);
        }
        finally
        {
            // Always advance — even on success. Windows takes 30-60s to actually tear down
            // after InitiateSystemShutdownEx returns, and during that window this loop would
            // tick again and re-issue the API, getting ERROR_SHUTDOWN_IN_PROGRESS (1115) and
            // a spurious Event 1009. Advancing means the next tick is waiting for tomorrow's
            // slot, so the in-flight shutdown completes quietly.
            AdvancePastCurrentSlot();
        }
    }

    private bool IsClientIdle()
        => _idleMinutes >= _policy.IdleThresholdMinutes && _policy.IdleThresholdMinutes > 0;

    private void RefreshPolicy()
    {
        try
        {
            _policy = _policyReader.Read();
            _lastPolicyRead = _time.GetLocalNow();
        }
        catch (Exception ex)
        {
            _logger.LogError(EventIds.RegistryReadFailed, ex,
                "Registry read failed: {Message}", ex.Message);
        }
    }

    private void RecomputeNextAction()
        => _nextAction = _scheduler.GetNextAction(_policy, _time.GetLocalNow().LocalDateTime);

    private void AdvancePastCurrentSlot()
    {
        var basis = (_nextAction ?? _time.GetLocalNow().LocalDateTime).AddMinutes(1);
        _nextAction = _scheduler.GetNextAction(_policy, basis);
        _postpones.Reset();
        _warningBroadcastFor = null;
        _teamsDeferral.Reset();
    }

    private bool DueForPolicyRefresh()
        => _time.GetLocalNow() - _lastPolicyRead >= PolicyRefreshInterval;

    private void WarnIfNoActiveDays()
    {
        if (_policy.Enabled && _policy.ActiveDays == DayFlags.None)
        {
            _logger.LogWarning(EventIds.NoActiveDays, "No active days configured — scheduling disabled");
        }
    }

    private async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero) return;
        try { await Task.Delay(delay, _time, ct); }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private static TimeSpan MinSpan(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private string FormatNext() => _nextAction?.ToString("O") ?? "(none)";
}
