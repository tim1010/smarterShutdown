using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmarterShutdown.Core.IPC;
using SmarterShutdown.Core.Idle;
using SmarterShutdown.Core.Teams;
using SmarterShutdown.Notifier.Tray;

namespace SmarterShutdown.Notifier;

public partial class App : Application
{
    private IHost? _host;
    private CancellationTokenSource? _cts;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _cts = new CancellationTokenSource();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.AddDebug();
        builder.Services.AddSingleton<IPipeClient>(sp =>
            new NamedPipeClient(sp.GetRequiredService<ILogger<NamedPipeClient>>()));
        builder.Services.AddSingleton<IIdleDetector, Win32IdleDetector>();
        builder.Services.AddSingleton<ITeamsDetector, Win32TeamsDetector>();
        builder.Services.AddSingleton<TrayIconManager>();
        builder.Services.AddSingleton<MessageRouter>();
        builder.Services.AddSingleton<StatusReporter>();

        _host = builder.Build();
        await _host.StartAsync(_cts.Token);

        var tray = _host.Services.GetRequiredService<TrayIconManager>();
        tray.Show();

        var client = _host.Services.GetRequiredService<IPipeClient>();
        await client.StartAsync(_cts.Token);

        var router = _host.Services.GetRequiredService<MessageRouter>();
        var reporter = _host.Services.GetRequiredService<StatusReporter>();
        _ = Task.Run(() => router.RunAsync(_cts.Token));
        _ = Task.Run(() => reporter.RunAsync(_cts.Token));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _cts?.Cancel();
        if (_host is not null)
        {
            try { await _host.StopAsync(); }
            finally { _host.Dispose(); }
        }
        _cts?.Dispose();
        base.OnExit(e);
    }
}
