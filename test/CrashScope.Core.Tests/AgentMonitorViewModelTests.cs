using CrashScope.Contracts;
using CrashScope.Core.Ipc;
using CrashScope.Core.Monitoring;
using CrashScope.Desktop;
using Xunit;

namespace CrashScope.Core.Tests;

public sealed class AgentMonitorViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crashscope-vm-{Guid.NewGuid():N}");

    public AgentMonitorViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* Windows may still hold a pipe handle for a moment. */ }
    }

    [Fact]
    public void CatalogAndTelemetry_MergeIntoRows_WithUnits()
    {
        var vm = new AgentMonitorViewModel();
        vm.ApplyCatalog(new CatalogPayload(
        [
            new SensorEntryDto(0, "Cpu", "AMD CPU", "Load", "CPU Core #1", "/amdcpu/0/load/1"),
            new SensorEntryDto(1, "Cpu", "AMD CPU", "Temperature", "Core (Tctl/Tdie)", "/amdcpu/0/temperature/0"),
            new SensorEntryDto(2, "GpuAmd", "AMD Radeon RX 9070 XT", "Clock", "GPU Core", "/gpu/0/clock/0")
        ]));

        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(2, vm.Rows.GroupBy(r => r.HardwareName).Count());

        vm.ApplyTelemetry(new TelemetryPayload(DateTimeOffset.Now, [42.5f, 61f, 2103f, 99f]));
        Assert.Equal("42.5%", vm.Rows[0].ValueText);
        Assert.Equal("61°C", vm.Rows[1].ValueText);
        Assert.Equal("2103MHz", vm.Rows[2].ValueText);

        vm.ApplyTelemetry(new TelemetryPayload(DateTimeOffset.Now, [null, null, null]));
        Assert.Equal("-", vm.Rows[0].ValueText);
    }

    [Fact]
    public async Task Connect_DisconnectAndEvents_AgainstLiveServer()
    {
        var stateDir = Path.Combine(_root, "state");
        var engine = new RecorderEngine(
            Path.Combine(_root, "session"), stateDir, Path.Combine(_root, "incidents"));
        var server = new AgentRpcServer(engine, stateDir, "crashscope-rpc-test-" + Guid.NewGuid().ToString("N")[..8]);
        using var _e = engine;
        using var _s = server;
        server.Start();

        var vm = new AgentMonitorViewModel();
        Assert.True(await vm.ConnectAsync(stateDir), vm.ConnectionError);
        Assert.True(vm.IsConnected);
        Assert.Equal(server.PipeName, vm.PipeName);
        Assert.False(vm.Recording);
        Assert.NotNull(vm.AgentVersion);

        engine.Start();
        Assert.True(await WaitForAsync(() => Task.FromResult(vm.Recording)), "vm never saw recording status");

        engine.Stop();
        Assert.True(await WaitForAsync(() => Task.FromResult(!vm.Recording)), "vm never saw stopped status");

        vm.Disconnect();
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task Connect_MissingEndpoint_ReportsFriendlyError()
    {
        var emptyDir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(emptyDir);
        var vm = new AgentMonitorViewModel();

        var ok = await vm.ConnectAsync(emptyDir);

        Assert.False(ok);
        Assert.Contains("rpc-endpoint", vm.ConnectionError);
        Assert.Contains("not running", vm.ConnectionError);
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 20000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (await condition().ConfigureAwait(false))
                return true;
            await Task.Delay(100).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }
}
