using LibreHardwareMonitor.Hardware;
using CrashScope.Core.Models;

namespace CrashScope.Core.Monitoring;

public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly List<SensorBinding> _bindings = new();

    private static readonly HashSet<SensorType> AllowedSensorTypes =
    [
        SensorType.Temperature,
        SensorType.Load,
        SensorType.Clock,
        SensorType.Power,
        SensorType.Voltage,
        SensorType.Fan,
        SensorType.Data,
        SensorType.SmallData,
        SensorType.Control
    ];

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = true
        };
        _computer.Open();
        _computer.Accept(_visitor);

        foreach (var hardware in _computer.Hardware)
            BindSensors(hardware);

        Catalog = _bindings
            .Select((binding, id) => new SensorCatalogEntry(
                id,
                binding.Hardware.HardwareType.ToString(),
                binding.Hardware.Name,
                binding.Sensor.SensorType.ToString(),
                binding.Sensor.Name,
                binding.Sensor.Identifier.ToString()))
            .ToArray();
    }

    public IReadOnlyList<SensorCatalogEntry> Catalog { get; }

    public IReadOnlyList<float?> CaptureValues()
    {
        _computer.Accept(_visitor);
        var values = new float?[_bindings.Count];
        for (var i = 0; i < _bindings.Count; i++)
            values[i] = _bindings[i].Sensor.Value;
        return values;
    }

    private void BindSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (AllowedSensorTypes.Contains(sensor.SensorType))
                _bindings.Add(new SensorBinding(hardware, sensor));
        }

        foreach (var subHardware in hardware.SubHardware)
            BindSensors(subHardware);
    }

    public void Dispose() => _computer.Close();

    private sealed record SensorBinding(IHardware Hardware, ISensor Sensor);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitParameter(IParameter parameter) { }
        public void VisitSensor(ISensor sensor) { }
    }
}
