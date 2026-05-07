using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmarterShutdown.Core.IPC;
using SmarterShutdown.Core.Idle;
using SmarterShutdown.Core.Teams;

namespace SmarterShutdown.Notifier;

/// <summary>
/// Periodically reports user-session state (idle minutes + Teams call active) to the Service
/// so it can apply IdleWarningMinutes / TeamsDefer policy. The Service can't read either of
/// these itself — IdleDetector returns nothing useful from session 0, and Teams audio sessions
/// only enumerate from inside the user session.
/// </summary>
public sealed class StatusReporter
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IPipeClient _client;
    private readonly IIdleDetector _idle;
    private readonly ITeamsDetector _teams;
    private readonly ILogger<StatusReporter> _logger;

    public StatusReporter(IPipeClient client, IIdleDetector idle, ITeamsDetector teams, ILogger<StatusReporter> logger)
    {
        _client = client;
        _idle = idle;
        _teams = teams;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var idleMinutes = (int)_idle.GetIdleTime().TotalMinutes;
                var teamsActive = _teams.IsInCall();
                await _client.SendAsync(
                    new PipeMessage
                    {
                        Type = MessageType.TeamsCallStatus,
                        TeamsCallActive = teamsActive,
                        IdleMinutes = idleMinutes,
                    },
                    ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Status report failed; will retry on next interval");
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
