using CrashScope.Service;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CrashScope";
});

builder.Services.AddHostedService<SessionRecorderWorker>();

builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "CrashScope";
    settings.LogName = "Application";
});

if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog();
}

var host = builder.Build();
host.Run();