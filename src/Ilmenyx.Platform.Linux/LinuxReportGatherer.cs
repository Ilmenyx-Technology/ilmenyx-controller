// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Builds the data of the general report from read-only local sources (/proc, /sys, /etc and the optional nvidia-smi query).
/// No SMART query, benchmark, network test or load runs here; SMART results appear only if the user imported one.
/// Every free-text value goes through <see cref="ReportTextPrivacy"/>, which minimizes obvious identifiers (not anonymization).
/// </summary>
public static class LinuxReportGatherer
{
    public static async Task<ReportData> GatherAsync(string appVersion, MemoryInventory? ram, GpuSnapshot? gpuSnapshot, IReadOnlyList<SensorMeasurement> sensors,
        IReadOnlyDictionary<string, SmartDiagnostic> smart, HealthCard? network, IEnumerable<BenchRunEntry> history, CancellationToken ct,
        string procRoot = "/proc", string sysRoot = "/sys", string etcRoot = "/etc")
    {
        ct.ThrowIfCancellationRequested();
        var ids = PciIds.Load();
        var system = await Task.Run(() => new LinuxSystemInfoCollector(procRoot, sysRoot, etcRoot).Collect(ram, gpuSnapshot, ids), ct);
        var smi = await LinuxGpuCollector.QuerySmiAsync(ct);
        var gpus = await Task.Run(() => new LinuxGpuCollector().Collect(ids, smi), ct);
        string mounts = "";
        try { mounts = File.ReadAllText(Path.Combine(procRoot, "self/mounts")); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var notes = new List<string>();
        List<HealthCard> disks = [], batteries = [];
        try { disks = new LinuxDiskHealthReader(sysRoot).Read(mounts, smart).Select(DiskHealthEvaluator.Evaluate).Select(Redact).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notes.Add(L.T("Discos indisponíveis: ") + Scrub(e.Message)); }
        var power = Redact(sensors, out var rows);
        try
        {
            var readout = new LinuxPowerReader(sysRoot).Read();
            string? plan = await LinuxPowerReader.ActiveProfileAsync(ct);
            batteries = BatteryEvaluator.Evaluate(readout.Batteries, readout.MainsOnline, plan, power).Select(Redact).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notes.Add(L.T("Energia indisponível: ") + Scrub(e.Message)); }
        notes.AddRange(new[] { "SMART: " + (smart.Count == 0 ? L.T("não consultado (nenhum relatório importado).") : L.T($"{smart.Count} relatório(s) importado(s) pelo usuário.")) });
        if (gpus.Count == 0) notes.Add(L.T("Nenhuma GPU PCI listada pelo inventário DRM."));
        return new ReportData
        {
            AppVersion = appVersion, Date = DateTimeOffset.Now,
            System = system.Select(Redact).ToList(),
            Gpus = gpus.Select(g => new ReportGpu(Scrub(g.Card.Name), Scrub(g.Card.Subtitle), g.Card.Sections.Select(Redact).ToList())).ToList(),
            Disks = disks, Power = batteries, Network = network is null ? [] : [Redact(network)], Sensors = rows,
            Benchmarks = history.Take(50).Select(h => h with { Summary = Scrub(h.Summary) }).ToList(), Notes = notes
        };
    }

    private static string Scrub(string text) => text.Length == 0 ? text : ReportTextPrivacy.Redact(text.Length > 512 ? text[..512] : text);

    private static InfoNode Redact(InfoNode node)
    {
        var copy = new InfoNode { Header = Scrub(node.Header), Value = Scrub(node.Value) };
        foreach (var child in node.Children) copy.Children.Add(Redact(child));
        return copy;
    }

    private static HealthCard Redact(HealthCard c) => new()
    {
        Title = Scrub(c.Title), Meta = Scrub(c.Meta), Device = c.Device, Verdict = c.Verdict, Label = c.Label, Reasons = Scrub(c.Reasons),
        Metrics = c.Metrics.Select(m => new KeyValuePair<string, string>(Scrub(m.Key), Scrub(m.Value))).ToList()
    };

    private static List<KeyValuePair<string, string>> Redact(IReadOnlyList<SensorMeasurement> sensors, out List<ReportSensorRow> rows)
    {
        rows = [];
        var power = new List<KeyValuePair<string, string>>();
        foreach (var s in sensors)
        {
            if (s.Reading.Value is not double v) continue;
            string value = v.ToString("0.##", CultureInfo.InvariantCulture) + (s.Reading.Unit.Length > 0 ? " " + s.Reading.Unit : "");
            rows.Add(new(Scrub(s.Chip), Scrub(s.Label), value, s.Quality));
            if (s.Channel.StartsWith("power", StringComparison.Ordinal) && v > 0) power.Add(new($"{s.Chip} · {s.Label}", v.ToString("0.0", CultureInfo.InvariantCulture) + " W"));
        }
        return power;
    }
}
