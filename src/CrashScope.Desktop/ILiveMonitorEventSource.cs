using CrashScope.Core.Models;

namespace CrashScope.Desktop;

/// <summary>
/// Informational event stream used by the live monitor. An injectable source lets the
/// compiled page be exercised with synthetic events without starting the recorder.
/// </summary>
public interface ILiveMonitorEventSource
{
    event Action<ForegroundEvent>? ForegroundChanged;
    event Action<ProcessEvent>? ProcessEventReceived;
    event Action<GpuEngineSample>? GpuEngineUpdated;
    event Action<DisplayTopologyEvent>? DisplayChanged;
    event Action<PowerSchemeEvent>? PowerSchemeChanged;
    event Action<RemoteSessionEvent>? RemoteSessionChanged;
}
