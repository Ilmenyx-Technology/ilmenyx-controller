// New Ilmenyx additions: see LICENSING.md.
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class GeneralReportTests
{
    private static ReportData Data()
    {
        var cpu = new InfoNode { Header = "Processador" };
        cpu.Add("Modelo", "<script>alert(1)</script> Ryzen");
        cpu.Add("Número de série", "SN-SECRET-1");
        cpu.Add("Nome do computador (local; omitido dos relatórios)", "HOST-SECRET");
        cpu.Add("Serial do módulo", "SN-SECRET-2");
        var group = cpu.Add("Cache"); group.Add("L3", "32 MiB"); group.Add("Serial interno", "SN-SECRET-3");
        return new ReportData
        {
            AppVersion = "0.29.0", Date = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero), System = [cpu],
            Gpus = [new ReportGpu("GPU <b>", "sub", [new InfoNode { Header = "Geral" }])],
            Disks = [new HealthCard { Title = "nvme0n1 <img src=x>", Meta = "NVMe", Verdict = HealthVerdict.Warning, Reasons = "Desgaste", Metrics = [new("Temp", "40 °C")] }],
            Sensors = [new ReportSensorRow("coretemp", "Package", "55 °C", "ok")],
            Benchmarks = [new BenchRunEntry(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), "CPU", "multi 5,400", 5400)],
            Notes = ["SMART: não consultado."]
        };
    }

    [Fact]
    public void HtmlEscapesEverythingAndHasNoScriptOrRemoteResource()
    {
        string html = GeneralReport.Html(Data());
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img src=x&gt;", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.Contains("não comparáveis", html);
    }

    [Fact]
    public void SerialNumbersAreOmittedFromHtmlAndJson()
    {
        string html = GeneralReport.Html(Data()), json = GeneralReport.Json(Data());
        foreach (string secret in new[] { "HOST-SECRET", "SN-SECRET-1", "SN-SECRET-2", "SN-SECRET-3" })
        {
            Assert.DoesNotContain(secret, html);
            Assert.DoesNotContain(secret, json);
        }
        Assert.Contains("32 MiB", html);
    }

    [Fact]
    public void JsonIsValidAndKeepsSections()
    {
        using var doc = JsonDocument.Parse(GeneralReport.Json(Data()));
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("formato").GetString());
        Assert.Equal(1, root.GetProperty("discos").GetArrayLength());
        Assert.Equal("ATENÇÃO", root.GetProperty("discos")[0].GetProperty("estado").GetString());
        Assert.Equal(1, root.GetProperty("sensores").GetArrayLength());
        Assert.False(root.TryGetProperty("computador", out _));
    }

    [Fact]
    public async Task ExportCreatesNewFileAndNeverOverwrites()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-report-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "r.html");
            await GeneralReport.ExportAsync(Data(), path, true);
            Assert.Contains("Ilmenyx", File.ReadAllText(path));
            await Assert.ThrowsAnyAsync<IOException>(() => GeneralReport.ExportAsync(Data(), path, true));
            Assert.Single(Directory.GetFiles(dir));
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GeneralReport.ExportAsync(Data(), Path.Combine(dir, "c.json"), false, cts.Token));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task GathererRedactsIdentifiersAndReadsNoSmart()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilmenyx-gather-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "proc/self")); Directory.CreateDirectory(Path.Combine(root, "sys")); Directory.CreateDirectory(Path.Combine(root, "etc"));
        try
        {
            File.WriteAllText(Path.Combine(root, "proc/cpuinfo"), "processor\t: 0\nmodel name\t: Fixture CPU\n");
            File.WriteAllText(Path.Combine(root, "proc/meminfo"), "MemTotal:       8000000 kB\nMemAvailable:   4000000 kB\n");
            var sensors = new[] { new SensorMeasurement("d", "chip", "temp1", "label mac=aa:bb:cc:dd:ee:ff", DateTimeOffset.Now, "ok", new Reading<double?>(42.5, "°C", "x", "available", null)) };
            var history = new[] { new BenchRunEntry(DateTimeOffset.Now, "OC", "config em /home/pessoa/segredo", 1) };
            var data = await LinuxReportGatherer.GatherAsync("0.29.0", null, null, sensors, new Dictionary<string, SmartDiagnostic>(), null, history, CancellationToken.None,
                Path.Combine(root, "proc"), Path.Combine(root, "sys"), Path.Combine(root, "etc"));
            Assert.DoesNotContain("aa:bb:cc", data.Sensors[0].Name);
            Assert.DoesNotContain("/home/pessoa", data.Benchmarks[0].Summary);
            Assert.Contains(data.Notes, n => n.Contains("não consultado"));
            Assert.Empty(data.Network);
        }
        finally { Directory.Delete(root, true); }
    }
}
