using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using Microsoft.Extensions.Logging;
using SmarterShutdown.Core.IPC;
using SmarterShutdown.Notifier.Tray;
using SmarterShutdown.Notifier.Views;

namespace SmarterShutdown.Notifier;

public sealed class MessageRouter
{
    private const string DefaultWarningMessage = "Please save your work.";

    private readonly IPipeClient _client;
    private readonly TrayIconManager _tray;
    private readonly ILogger<MessageRouter> _logger;
    private CountdownWindow? _activeCountdown;

    public MessageRouter(IPipeClient client, TrayIconManager tray, ILogger<MessageRouter> logger)
    {
        _client = client;
        _tray = tray;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _client.Incoming.ReadAllAsync(ct))
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Handle(msg));
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MessageRouter loop ended unexpectedly");
        }
    }

    private void Handle(PipeMessage msg)
    {
        switch (msg.Type)
        {
            case MessageType.ShutdownPending:
            case MessageType.ShutdownPendingIdle:
                ShowOrUpdateCountdown(msg);
                _tray.SetTooltip($"SmarterShutdown — action at {msg.ScheduledAt:HH:mm}");
                break;

            case MessageType.PostponeAck:
                if (msg.ScheduledAt is not null)
                {
                    _activeCountdown?.UpdateScheduledAt(
                        msg.ScheduledAt.Value,
                        msg.PostponesRemaining,
                        msg.MaxPostpones,
                        msg.PostponeAllowed ?? true);
                    _tray.SetTooltip($"SmarterShutdown — postponed until {msg.ScheduledAt:HH:mm}");
                }
                break;

            case MessageType.SuspendAck:
                _activeCountdown?.Close();
                _activeCountdown = null;
                _tray.SetTooltip("SmarterShutdown — suspended for tonight");
                break;

            case MessageType.DeferredTeams:
                _tray.SetTooltip(msg.Message ?? "SmarterShutdown — deferred (Teams call)");
                break;

            case MessageType.PolicyRefreshed:
                _tray.SetTooltip("SmarterShutdown");
                break;

            default:
                _logger.LogDebug("Ignoring incoming pipe message {Type}", msg.Type);
                break;
        }
    }

    private void ShowOrUpdateCountdown(PipeMessage msg)
    {
        if (msg.ScheduledAt is null) return;

        var allowed = msg.PostponeAllowed ?? true;
        var message = string.IsNullOrWhiteSpace(msg.Message) ? DefaultWarningMessage : msg.Message!;

        if (_activeCountdown is null)
        {
            _activeCountdown = new CountdownWindow(
                _client,
                msg.ScheduledAt.Value,
                message,
                msg.PostponesRemaining,
                msg.MaxPostpones,
                allowed);
            _activeCountdown.Closed += (_, _) => _activeCountdown = null;
            _activeCountdown.Show();
        }
        else
        {
            _activeCountdown.UpdateScheduledAt(
                msg.ScheduledAt.Value,
                msg.PostponesRemaining,
                msg.MaxPostpones,
                allowed);
        }
    }
}
