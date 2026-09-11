using System.Collections.ObjectModel;

namespace CrashScope.App.ViewModels;

public sealed class HardwareGroupViewModel
{
    public required string HardwareName { get; init; }
    public required string HardwareType { get; init; }
    public ObservableCollection<SensorValueViewModel> Sensors { get; } = new();
}

public sealed class SensorValueViewModel
{
    public required string SensorName { get; init; }
    public required string SensorType { get; init; }
    public required string ValueText { get; init; }
}
