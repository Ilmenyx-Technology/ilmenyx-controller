// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Reused components keep their existing terms.
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;
namespace Ilmenyx.Tests;

public class ValidationHtmlViewModelTests
{
    private sealed class Missing : ILinuxFiles
    {
        public IReadOnlyList<string> BlockDevices()=>[];
        public Task<string> ReadAsync(string path,CancellationToken ct)=>throw new FileNotFoundException();
    }
    private sealed class CountingHardware : IHardwareInfoProvider
    {
        public int Calls;
        public Task<HardwareSnapshot> CollectAsync(CancellationToken ct=default)
        { Calls++;return new LinuxHardwareInfoProvider(new Missing()).CollectAsync(ct); }
    }
    [Theory]
    [InlineData("cpu")] [InlineData("memory")] [InlineData("sim")]
    public async Task HtmlUsesExistingReportAndPreservesAtomicCancelAndOverwriteSemantics(string kind)
    {
        var hardware=new CountingHardware();
        using var cpu=new CpuValidationViewModel(hardware,new CpuBenchmarkRunner());
        using var memory=new MemoryValidationViewModel(hardware,new MemoryValidationRunner());
        using var sim=new GameSimValidationViewModel(hardware,new GameSimValidationRunner());
        Func<Task> start=kind switch{"cpu"=>()=>cpu.StartAsync(),"memory"=>()=>memory.StartAsync(),_=>()=>sim.StartAsync()};
        Func<string,CancellationToken,Task> export=kind switch{"cpu"=>cpu.ExportHtmlAsync,"memory"=>memory.ExportHtmlAsync,_=>sim.ExportHtmlAsync};
        Func<bool> ready=kind switch{"cpu"=>()=>cpu.CanStart && cpu.CanExport,"memory"=>()=>memory.CanStart && memory.CanExport,_=>()=>sim.CanStart && sim.CanExport};
        var directory=Path.Combine(Path.GetTempPath(),"ilmenyx-html-vm-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var file=Path.Combine(directory,"result.html");
            await export(file,default);Assert.False(File.Exists(file));Assert.Equal(0,hardware.Calls);
            await start();Assert.True(ready());Assert.Equal(1,hardware.Calls);
            var expected=kind switch{"cpu"=>ValidationHtmlReport.Render(cpu.Report!),"memory"=>ValidationHtmlReport.Render(memory.Report!),_=>ValidationHtmlReport.Render(sim.Report!)};
            await export(file,default);Assert.Equal(expected,await File.ReadAllTextAsync(file));
            await export(file,default);Assert.Equal(expected,await File.ReadAllTextAsync(file));
            using var stop=new CancellationTokenSource();stop.Cancel();
            await export(Path.Combine(directory,"cancelled.html"),stop.Token);
            Assert.Single(Directory.GetFiles(directory));Assert.Equal(1,hardware.Calls);Assert.True(ready());
        }
        finally{Directory.Delete(directory,true);}
    }
}
