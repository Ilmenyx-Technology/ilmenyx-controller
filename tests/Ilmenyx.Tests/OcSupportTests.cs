// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class OcSupportTests
{
    [Fact]
    public void GuideAdaptsToBoardVendorAndCpu()
    {
        var amd = OcGuideBuilder.Build("Gigabyte Technology Co., Ltd.", "B650 AORUS", "AMD Ryzen 7 7800X3D 8-Core Processor");
        Assert.Equal(6, amd.Steps.Count);
        Assert.Contains("Tweaker", amd.Steps[1].Where);
        Assert.Contains("EXPO1", amd.Steps[1].Where);
        Assert.Contains("Curve Optimizer", amd.Steps[3].Title);
        Assert.Contains("GIGABYTE B650 AORUS", amd.Title);
        var intel = OcGuideBuilder.Build("ASUSTeK COMPUTER INC.", "PRIME Z790", "Intel(R) Core(TM) i7-13700K");
        Assert.Equal(3, intel.Steps.Count);
        Assert.Contains("Intel", intel.Steps[2].Title);
        Assert.Contains("AI Tweaker", intel.Steps[1].Where);
        var msi = OcGuideBuilder.Build("Micro-Star International", "B550", "AMD Ryzen 5 5600");
        Assert.Contains("A-XMP", msi.Steps[1].Where);
        var unknown = OcGuideBuilder.Build("", "", "AMD Ryzen 5 5600");
        Assert.Contains("procure EXPO", unknown.Steps[1].Where);
        Assert.Contains("não altera a BIOS", intel.Intro);
    }

    [Fact]
    public void KernelLogParserCountsHardwareErrorsByKind()
    {
        const string log = """
            2026-10-22T10:00:00+00:00 host kernel: mce: [Hardware Error]: Machine check events logged
            2026-10-22T10:00:01+00:00 host kernel: EDAC MC0: 1 CE memory read error on DIMM_A1
            2026-10-22T10:00:02+00:00 host kernel: pcieport 0000:00:1c.0: AER: Corrected error received
            2026-10-22T10:00:03+00:00 host kernel: NVRM: Xid (PCI:0000:01:00): 79, GPU has fallen off the bus
            2026-10-22T10:00:04+00:00 host kernel: amdgpu 0000:03:00.0: amdgpu: GPU reset begin!
            2026-10-22T10:00:05+00:00 host kernel: usb 1-1: new device
            """;
        var e = KernelLogParser.Parse(log);
        Assert.Equal(1, e.Cpu);
        Assert.Equal(1, e.Memory);
        Assert.Equal(1, e.Pcie);
        Assert.Equal(2, e.Gpu);
        Assert.Equal(5, e.Total);
        Assert.Contains("GPU reset", e.Last);
        Assert.Equal(0, KernelLogParser.Parse("kernel: usb 1-1: new device\nkernel: Linux version").Total);
    }

    [Fact]
    public void KernelLogLastLineIsBoundedAndHasNoControlCharacters()
    {
        var e = KernelLogParser.Parse("mce: " + new string('x', 400) + "\u001b[31m");
        Assert.True(e.Last!.Length <= KernelLogParser.MaxLastCharacters + 1);
        Assert.DoesNotContain('\u001b', e.Last);
    }

    [Theory]
    [InlineData(false, 15, 95, false)]
    [InlineData(true, 7, 95, false)]
    [InlineData(true, 15, 50, false)]
    [InlineData(true, 15, 110, false)]
    [InlineData(true, 15, 95, true)]
    public void OptionsRejectUnsafeValues(bool cpu, int minutes, double limit, bool valid) =>
        Assert.Equal(valid, OcCheckOptions.Validate(new(cpu, TimeSpan.FromMinutes(minutes), "x", limit)) is null);

    private sealed class FakeEngine : IStressEngine
    {
        public bool Running { get; private set; }
        public long MemoryErrors { get; set; }
        public long MemoryPasses { get; set; } = 3;
        public long MemoryAllocatedBytes => 1 << 30;
        public bool Started, Stopped, FailToStart;
        public void Start(bool cpu, bool ram, double ramFraction, long availableMemoryBytes)
        { if (FailToStart) throw new InvalidOperationException("pouca RAM"); Started = Running = true; }
        public void Stop() { Stopped = true; Running = false; }
    }

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public Func<TimeSpan, CancellationToken, Task> Delay => (t, ct) => { ct.ThrowIfCancellationRequested(); Now += t; OnTick?.Invoke(); return Task.CompletedTask; };
        public Action? OnTick;
    }

    private static Task<OcCheckResult> Run(FakeEngine engine, Clock clock, Func<double?> temp, Func<DateTimeOffset, Task<KernelHardwareErrors>>? errors = null, CancellationToken ct = default, Func<CancellationToken, Task<double?>>? score = null) =>
        OcCheckRunner.RunAsync(new(true, TimeSpan.FromMinutes(5), "teste"), engine, 8L << 30, score ?? (_ => Task.FromResult<double?>(5400)), temp,
            errors ?? (_ => Task.FromResult(new KernelHardwareErrors(0, 0, 0, 0, null, null))), clock.Delay, () => clock.Now, ct);

    [Fact]
    public async Task CleanRunIsStableAndStopsTheEngine()
    {
        var engine = new FakeEngine(); var r = await Run(engine, new(), () => 70);
        Assert.Equal(OcCheckStatus.Completed, r.Status);
        Assert.True(r.Stable);
        Assert.Null(r.FailureReason);
        Assert.Equal(5400, r.CpuScore);
        Assert.Equal(70, r.MaxCpuTemp);
        Assert.True(engine.Started && engine.Stopped);
    }

    [Fact]
    public async Task TemperatureOverTheLimitMakesItUnstableAndCutsEarly()
    {
        var engine = new FakeEngine(); var clock = new Clock(); double temp = 70;
        clock.OnTick = () => { if (clock.Now > new DateTimeOffset(2026, 10, 22, 12, 0, 30, TimeSpan.Zero)) temp = 97; };
        var r = await Run(engine, clock, () => temp);
        Assert.False(r.Stable);
        Assert.Contains("97", r.FailureReason);
        Assert.True(r.Elapsed < TimeSpan.FromMinutes(2));
        Assert.True(engine.Stopped);
    }

    [Fact]
    public async Task MemoryErrorsMakeItUnstable()
    {
        var engine = new FakeEngine(); var clock = new Clock();
        clock.OnTick = () => engine.MemoryErrors = 2;
        var r = await Run(engine, clock, () => 60);
        Assert.False(r.Stable);
        Assert.Contains("2 erros de memória", r.FailureReason);
    }

    [Fact]
    public async Task KernelHardwareErrorsMakeItUnstable()
    {
        var r = await Run(new FakeEngine(), new(), () => 60, _ => Task.FromResult(new KernelHardwareErrors(1, 0, 0, 0, "mce", null)));
        Assert.False(r.Stable);
        Assert.Contains("MCE", r.FailureReason);
    }

    [Fact]
    public async Task UnreadableKernelLogIsReportedNotTreatedAsClean()
    {
        var r = await Run(new FakeEngine(), new(), () => 60, _ => Task.FromResult(KernelHardwareErrors.Unavailable("sem permissão")));
        Assert.True(r.Stable);
        Assert.Contains("não foram verificados", r.Note);
    }

    [Fact]
    public async Task CancelledRunPublishesNoVerdict()
    {
        using var cts = new CancellationTokenSource(); var engine = new FakeEngine(); var clock = new Clock();
        clock.OnTick = () => cts.Cancel();
        var r = await Run(engine, clock, () => 60, null, cts.Token);
        Assert.Equal(OcCheckStatus.Cancelled, r.Status);
        Assert.Null(r.Stable);
        Assert.True(engine.Stopped);
    }

    [Fact]
    public async Task EngineRefusalIsUnavailableNotUnstable()
    {
        var r = await Run(new FakeEngine { FailToStart = true }, new(), () => 60);
        Assert.Equal(OcCheckStatus.Unavailable, r.Status);
        Assert.Null(r.Stable);
        Assert.Contains("pouca RAM", r.FailureReason);
    }

    [Fact]
    public async Task InvalidOptionsNeverStartTheEngine()
    {
        var engine = new FakeEngine();
        var r = await OcCheckRunner.RunAsync(new(true, TimeSpan.FromMinutes(7), ""), engine, 8L << 30, _ => Task.FromResult<double?>(null), () => 60,
            _ => Task.FromResult(new KernelHardwareErrors(0, 0, 0, 0, null, null)), (_, _) => Task.CompletedTask, () => DateTimeOffset.UnixEpoch, CancellationToken.None);
        Assert.Equal(OcCheckStatus.Unavailable, r.Status);
        Assert.False(engine.Started);
    }

    private const string Csv = "0, NVIDIA GeForce RTX 4070, 200.00, 200.00, 100.00, 220.00, 3105, 210\n1, Other, [N/A], 120.00, 60.00, 130.00, [N/A], 300\n";

    [Fact]
    public void NvidiaCsvIsParsedWithMissingFields()
    {
        var gpus = NvidiaTuning.Parse(Csv);
        Assert.Equal(2, gpus.Count);
        Assert.Equal(220, gpus[0].PowerMaxW);
        Assert.Equal(3105u, gpus[0].MaxClockMhz);
        Assert.Null(gpus[1].PowerLimitW);
        Assert.Null(gpus[1].MaxClockMhz);
        Assert.Empty(NvidiaTuning.Parse("garbage\n,,,"));
    }

    [Fact]
    public void NvidiaCommandsAreValidatedAndNeverExecuted()
    {
        var gpu = NvidiaTuning.Parse(Csv)[0];
        Assert.Equal(["sudo nvidia-smi -i 0 -pl 180", "sudo nvidia-smi -i 0 -lgc 210,2600"], NvidiaTuning.Commands(gpu, 180, 2600));
        Assert.Equal("sudo nvidia-smi -i 0 -rgc", NvidiaTuning.Commands(gpu, 200, null)[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => NvidiaTuning.Commands(gpu, 90, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => NvidiaTuning.Commands(gpu, 230, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => NvidiaTuning.Commands(gpu, 180, 100));
        // Real GeForce 940MX (driver 580.178.04): power.limit N/A with placeholder 200/0/200 W.
        var laptop = NvidiaTuning.Parse("0, NVIDIA GeForce 940MX, [N/A], 200.00, 0.00, 200.00, 1189, 135").Single();
        Assert.False(NvidiaTuning.SupportsPowerLimit(laptop)); Assert.Null(laptop.PowerDefaultW); Assert.Null(laptop.PowerMaxW);
        Assert.Equal(["sudo nvidia-smi -i 0 -lgc 210,889"], NvidiaTuning.Commands(laptop, null, 889));
        Assert.Throws<InvalidOperationException>(() => NvidiaTuning.Commands(laptop, 15, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => NvidiaTuning.Commands(gpu, 180, 9000));
        Assert.Equal(["sudo nvidia-smi -i 0 -pl 200", "sudo nvidia-smi -i 0 -rgc"], NvidiaTuning.ResetCommands(gpu));
        Assert.Throws<InvalidOperationException>(() => NvidiaTuning.Commands(new(0, "x", null, null, null, null, null, null), 100, null));
    }

    [Fact]
    public async Task KernelLogReaderReportsNoAccessWhenProbeIsEmpty()
    {
        var calls = new List<string>();
        var log = new LinuxKernelLog((args, _) => { calls.Add(string.Join(' ', args)); return Task.FromResult((0, "")); });
        var e = await log.ErrorsSinceAsync(DateTimeOffset.UtcNow);
        Assert.Equal(LinuxKernelLog.NoAccess, e.Error);
        Assert.Single(calls);
        Assert.DoesNotContain(calls, c => c.Contains("sudo"));
    }

    [Fact]
    public async Task KernelLogReaderParsesAndUsesUtcSince()
    {
        var calls = new List<string>();
        var log = new LinuxKernelLog((args, _) => { calls.Add(string.Join('|', args)); return Task.FromResult((0, args.Contains("--since") ? "kernel: mce: Hardware Error\n" : "x")); });
        var e = await log.ErrorsSinceAsync(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.FromHours(-3)));
        Assert.Equal(1, e.Cpu);
        Assert.Contains("2026-10-22 12:30:00 UTC", calls[1]);
    }

    [Fact]
    public async Task KernelLogReaderHandlesMissingJournalctl()
    {
        var log = new LinuxKernelLog((_, _) => throw new FileNotFoundException("journalctl não encontrado"));
        Assert.Contains("journalctl", (await log.ErrorsSinceAsync(DateTimeOffset.UtcNow)).Error);
    }

    [Fact]
    public void BoardIsReadFromFixture()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilmenyx-dmi-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "board_vendor"), "ASUSTeK COMPUTER INC.\n");
            File.WriteAllText(Path.Combine(root, "board_name"), "PRIME B650\n");
            Assert.Equal(("ASUSTeK COMPUTER INC.", "PRIME B650"), LinuxKernelLog.Board(root));
            Assert.Equal(("", ""), LinuxKernelLog.Board(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class OcGpuTests
{
    private sealed class Gpu : IGpuStressEngine
    {
        public string? UnavailableReason { get; set; }
        public string AdapterName => "Fake GPU";
        public bool SharesCpuThermals { get; set; }
        public bool Running { get; private set; }
        public double Fps => 60;
        private string? error;
        public string? Error { get => error; set { error = value; if (value is not null) Running = false; } }
        public int Starts, Stops;
        public void Start() { Running = true; Starts++; }
        public void Stop() { Running = false; Stops++; }
    }

    private sealed class Cpu : IStressEngine
    {
        public bool Running { get; private set; } public long MemoryErrors { get; set; } public long MemoryPasses => 1; public long MemoryAllocatedBytes => 1 << 30; public int Starts;
        public void Start(bool cpu, bool ram, double ramFraction, long availableMemoryBytes) { Running = true; Starts++; }
        public void Stop() => Running = false;
    }

    private static readonly Func<DateTimeOffset, Task<KernelHardwareErrors>> Clean = _ => Task.FromResult(new KernelHardwareErrors(0, 0, 0, 0, null, null));

    private static Task<OcCheckResult> Run(OcCheckOptions o, Cpu cpu, Gpu? gpu, Func<double?> cpuTemp, Func<double?>? gpuTemp = null, Action? tick = null)
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        return OcCheckRunner.RunAsync(o, cpu, 8L << 30, _ => Task.FromResult<double?>(5400), cpuTemp, Clean, (t, _) => { now += t; tick?.Invoke(); return Task.CompletedTask; }, () => now,
            CancellationToken.None, null, gpu, _ => Task.FromResult<double?>(480), gpuTemp);
    }

    [Fact]
    public async Task GpuOnlyRunNeverStartsTheCpuEngineAndPublishesTheGpuScore()
    {
        var cpu = new Cpu(); var gpu = new Gpu();
        var r = await Run(new(false, TimeSpan.FromMinutes(5), "g", Gpu: true), cpu, gpu, () => 60, () => 70);
        Assert.True(r.Stable);
        Assert.Equal(0, cpu.Starts);
        Assert.Equal(1, gpu.Starts); Assert.Equal(1, gpu.Stops);
        Assert.Equal(480, r.GpuScore); Assert.Null(r.CpuScore);
        Assert.Equal(70, r.MaxGpuTemp);
    }

    [Fact]
    public async Task CombinedRunStartsBothAndStopsBoth()
    {
        var cpu = new Cpu(); var gpu = new Gpu();
        var r = await Run(new(true, TimeSpan.FromMinutes(5), "x", Gpu: true), cpu, gpu, () => 60, () => 70);
        Assert.True(r.Stable); Assert.Equal(1, cpu.Starts); Assert.Equal(1, gpu.Starts); Assert.Equal(5400, r.CpuScore); Assert.Equal(480, r.GpuScore);
        Assert.False(cpu.Running); Assert.False(gpu.Running);
    }

    [Fact]
    public async Task GpuOverTemperatureMakesItUnstable()
    {
        var gpu = new Gpu(); double temp = 60;
        var r = await Run(new(false, TimeSpan.FromMinutes(5), "x", Gpu: true), new Cpu(), gpu, () => 60, () => temp, () => temp = 95);
        Assert.False(r.Stable); Assert.Contains("GPU chegou a 95", r.FailureReason); Assert.Equal(1, gpu.Stops);
    }

    [Fact]
    public async Task IntegratedGpuUsesTheCpuTemperature()
    {
        var gpu = new Gpu { SharesCpuThermals = true }; double cpuTemp = 60;
        var r = await Run(new(false, TimeSpan.FromMinutes(5), "x", Gpu: true), new Cpu(), gpu, () => cpuTemp, () => 10, () => cpuTemp = 92);
        Assert.False(r.Stable); Assert.Contains("GPU chegou a 92", r.FailureReason);
    }

    [Fact]
    public async Task GpuEngineFailureIsReportedAsUnstable()
    {
        var gpu = new Gpu();
        var r = await Run(new(false, TimeSpan.FromMinutes(5), "x", Gpu: true), new Cpu(), gpu, () => 60, () => 60, () => gpu.Error = "DEVICE_LOST");
        Assert.False(r.Stable);
    }

    [Fact]
    public async Task GpuEngineStoppedByItselfWithErrorIsNamed()
    {
        var gpu = new FailingGpu();
        var r = await Run(new(false, TimeSpan.FromMinutes(5), "x", Gpu: true), new Cpu(), null, () => 60, null, null, gpu);
        Assert.False(r.Stable); Assert.Contains("a GPU falhou (DEVICE_LOST)", r.FailureReason);
    }

    private sealed class FailingGpu : IGpuStressEngine
    {
        public string? UnavailableReason => null; public string AdapterName => "x"; public bool SharesCpuThermals => false; public double Fps => 0;
        public bool Running => false; public string? Error => "DEVICE_LOST"; public void Start() { } public void Stop() { }
    }

    private static Task<OcCheckResult> Run(OcCheckOptions o, Cpu cpu, Gpu? _, Func<double?> cpuTemp, Func<double?>? gpuTemp, Action? tick, IGpuStressEngine engine)
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        return OcCheckRunner.RunAsync(o, cpu, 8L << 30, _ => Task.FromResult<double?>(5400), cpuTemp, Clean, (t, _) => { now += t; tick?.Invoke(); return Task.CompletedTask; }, () => now,
            CancellationToken.None, null, engine, _ => Task.FromResult<double?>(480), gpuTemp);
    }

    [Fact]
    public async Task UnavailableGpuNeverStartsAnything()
    {
        var cpu = new Cpu();
        var r = await Run(new(true, TimeSpan.FromMinutes(5), "x", Gpu: true), cpu, new Gpu { UnavailableReason = "sem Vulkan" }, () => 60);
        Assert.Equal(OcCheckStatus.Unavailable, r.Status); Assert.Contains("sem Vulkan", r.FailureReason); Assert.Equal(0, cpu.Starts);
        var none = await Run(new(true, TimeSpan.FromMinutes(5), "x", Gpu: true), cpu, null, () => 60);
        Assert.Equal(OcCheckStatus.Unavailable, none.Status);
    }

    [Theory]
    [InlineData(false, false, 95, 88, false)]
    [InlineData(false, true, 95, 88, true)]
    [InlineData(true, true, 95, 50, false)]
    [InlineData(true, true, 95, 101, false)]
    [InlineData(true, false, 95, 10, true)]
    public void GpuOptionsAreValidated(bool cpu, bool gpu, double cpuLimit, double gpuLimit, bool valid) =>
        Assert.Equal(valid, OcCheckOptions.Validate(new(cpu, TimeSpan.FromMinutes(15), "x", cpuLimit, gpu, gpuLimit)) is null);
}
