using System;
using System.Drawing;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Application = System.Windows.Application;
using SmarterShutdown.Core.IPC;

namespace SmarterShutdown.Notifier.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly IPipeClient _pipeClient;
    private readonly NotifyIcon _notifyIcon;

    public TrayIconManager(IPipeClient pipeClient)
    {
        _pipeClient = pipeClient;

        _notifyIcon = new NotifyIcon
        {
            // Placeholder icon — installer will ship a real one alongside the executable.
            Icon = SystemIcons.Information,
            Text = "SmarterShutdown",
            Visible = false,
        };

        _notifyIcon.ContextMenuStrip = BuildMenu();
    }

    public void Show() => _notifyIcon.Visible = true;

    public void SetTooltip(string text)
    {
        // NotifyIcon tooltip caps at 63 characters; truncate cleanly to avoid throwing.
        _notifyIcon.Text = text.Length > 63 ? text[..60] + "..." : text;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // The "Suspend tonight's shutdown" item only appears when the Notifier process holds
        // the local-admin role in its token — that's the bar the spec sets for using it.
        if (IsLocalAdministrator())
        {
            menu.Items.Add("Suspend tonight's shutdown", null, OnSuspendClicked);
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Exit", null, OnExitClicked);
        return menu;
    }

    private async void OnSuspendClicked(object? sender, EventArgs e)
    {
        try
        {
            await _pipeClient.SendAsync(
                new PipeMessage
                {
                    Type = MessageType.SuspendRequest,
                    UserName = Environment.UserName,
                },
                CancellationToken.None);
        }
        catch
        {
            // Best-effort; the SuspendAck round trip will tell the user whether it landed.
        }
    }

    private void OnExitClicked(object? sender, EventArgs e) => Application.Current.Shutdown();

    private static bool IsLocalAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
