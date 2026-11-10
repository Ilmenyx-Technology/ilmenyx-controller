// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New test contribution: see LICENSING.md; original fixtures/components retain their own terms.
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;
using Xunit;
namespace Ilmenyx.Tests;

public class ValidationHtmlTests
{
    private sealed class Missing : ILinuxFiles
    {
        public IReadOnlyList<string> BlockDevices()=>[];
        public Task<string> ReadAsync(string path,CancellationToken ct)=>throw new FileNotFoundException();
    }
    private static Task<HardwareSnapshot> Hardware()=>new LinuxHardwareInfoProvider(new Missing()).CollectAsync();
    [Fact]
    public async Task ExistingReducedResultsRenderWithHashesProvenanceAndEscapedLimitations()
    {
        var hardware=await Hardware();const string payload="</section><script>alert(1)</script>";
        var cpu=(await new CpuBenchmarkRunner().RunAsync(new())).Result;
        var memory=await new MemoryValidationRunner().RunAsync(new());
        var sim=await new GameSimValidationRunner().RunAsync(new());
        var results=new[]{
            (ValidationHtmlReport.Render(new CpuBenchmarkReport("1.0",hardware,cpu with{Limitations=[payload]})),cpu.ImageSha256),
            (ValidationHtmlReport.Render(new MemoryValidationReport("1.0",hardware,memory with{Limitations=[payload]})),memory.CopySha256!),
            (ValidationHtmlReport.Render(new GameSimValidationReport("1.0",hardware,sim with{Limitations=[payload]})),sim.StateSha256!)
        };
        foreach(var (html,hash) in results)
        {
            Assert.Contains("id=\"validation-result\"",html);Assert.Contains(hash,html);Assert.Contains(cpu.ReferenceCommit,html);
            Assert.Contains("Ilmenyx",html);Assert.Contains("Ilmenyx Technology",html);Assert.Contains("GustavoReset",html);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(payload),html);Assert.DoesNotContain("<script",html);
            Assert.DoesNotContain("<img",html);Assert.DoesNotContain("<iframe",html);Assert.Contains("default-src 'none'",html);
            Assert.Contains("não executa carga adicional",html);Assert.Contains("Sem pontuação",html);
        }
        Assert.Throws<ArgumentException>(()=>ValidationHtmlReport.Render(new CpuBenchmarkReport("1.0",hardware,cpu with{Workload=CpuBenchmarkRunner.DescribeWorkload(CpuWorkloadProfile.Full)})));
    }
    [Fact]
    public async Task CancelledResultShowsUnavailableHashWithoutInventingMetrics()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();
        var result=await new GameSimValidationRunner().RunAsync(new(),stop.Token);
        var report=new GameSimValidationReport("1.0",await Hardware(),result);
        var html=ValidationHtmlReport.Render(report);
        Assert.Contains("Cancelled",html);Assert.Contains("indisponível",html);Assert.Contains("sem FPS",html);
        Assert.Throws<OperationCanceledException>(()=>ValidationHtmlReport.Render(report,stop.Token));
    }
    [Fact]
    public async Task AtomicExportRefusesOverwriteAndCleansOwnTemporaryFile()
    {
        var directory=Path.Combine(Path.GetTempPath(),"ilmenyx-validation-html-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var report=new MemoryValidationReport("1.0",await Hardware(),await new MemoryValidationRunner().RunAsync(new()));
            var file=Path.Combine(directory,"result.html");await ValidationHtmlReport.ExportAsync(report,file);
            var original=await File.ReadAllTextAsync(file);Assert.Equal(ValidationHtmlReport.Render(report),original);
            await Assert.ThrowsAsync<IOException>(()=>ValidationHtmlReport.ExportAsync(report,file));
            Assert.Equal(original,await File.ReadAllTextAsync(file));Assert.Single(Directory.GetFiles(directory));
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]
    public void CliHtmlIsExplicitAndCannotSelectFullLoad()
    {
        foreach(var name in new[]{"cpu","memory","sim"})
        {
            Assert.True(CliArguments.Parse(["bench",name,"--html"]).Html);
            Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench",name,"--html","--json"]));
        }
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","cpu","--profile","full","--allow-cpu-load","--html"]));
        Assert.Throws<ArgumentException>(()=>CliArguments.Parse(["bench","sim-full","--allow-cpu-load","--html"]));
    }
}
