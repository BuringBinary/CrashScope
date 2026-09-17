using CrashScope.Service;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CrashScope";
});

builder.Services.AddHostedService<SessionRecorderWorker>();

if (OperatingSystem.IsWindows())
{
    // Single EventLog sink: previously registered twice, so every message was
    // written to the Application log under two providers.
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "CrashScope";
        settings.LogName = "Application";
    });
}

var host = builder.Build();
host.Run();