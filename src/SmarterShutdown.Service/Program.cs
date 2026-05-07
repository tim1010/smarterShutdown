using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmarterShutdown.Core.IPC;
using SmarterShutdown.Core.Idle;
using SmarterShutdown.Core.Policy;
using SmarterShutdown.Core.Power;
using SmarterShutdown.Core.Scheduler;
using SmarterShutdown.Core.Shutdown;
using SmarterShutdown.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SmarterShutdown";
});

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "SmarterShutdown";
    settings.LogName = "Application";
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRegistryReader, Win32RegistryReader>();
builder.Services.AddSingleton<PolicyReader>();
builder.Services.AddSingleton<SchedulerEngine>();
builder.Services.AddSingleton<IBatteryStatus, Win32BatteryStatus>();
builder.Services.AddSingleton<IIdleDetector, Win32IdleDetector>();
builder.Services.AddSingleton<IShutdownExecutor, Win32ShutdownExecutor>();
builder.Services.AddSingleton<IPipeServer, NamedPipeServer>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
