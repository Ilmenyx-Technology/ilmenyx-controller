// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Method and limits: docs/meu-pc-roda.md.
using System.Globalization;

namespace Ilmenyx.Core;

/// <summary>What was read from this PC (read-only). Zero/null = could not be read; nothing is guessed from it.</summary>
public sealed record LocalPc(string CpuName, string GpuName, ulong GpuVramBytes, bool GpuIntegrated, long RamBytes, long? FreeBytes, string FreeLabel, bool VulkanLoader, string? GpuPciId = null);

/// <summary><c>NearMin</c>/<c>NearRec</c>: both sides are known and the PC is a little under the stated level (inside the margin of the data): neither a pass nor a fail.</summary>
public enum ReqStatus { Unknown, Below, MeetsMin, MeetsRec, NearMin, NearRec }
public enum CanRunVerdict { NoData, BelowMinimum, Minimum, Recommended }

/// <summary><paramref name="Counts"/>: false for items that only inform (system, graphics API) and never decide the verdict.</summary>
public sealed record CanRunItem(string Key, string Title, string Minimum, string Recommended, string Local, ReqStatus Status, string Note, bool Counts, RatingBasis? Basis = null, bool Shared = false);

public sealed record CanRunReport(IReadOnlyList<CanRunItem> Items, CanRunVerdict Verdict, IReadOnlyList<string> BelowTitles, int NotCompared, string Headline, IReadOnlyList<string>? NearTitles = null);

/// <summary>Compares published requirements with this PC, item by item. Pure: no I/O. Never estimates FPS.</summary>
public static class CanRunCompare
{
    /// <summary>Reported RAM is smaller than the installed amount (firmware/iGPU reserve): 8 GB shows as ~7.6 GiB. Accept 90 %.</summary>
    public const double RamTolerance = 0.90;
    /// <summary>Reported VRAM is a little below the nominal size ("8 GB" = 8176 MiB). Accept 95 %.</summary>
    public const double VramTolerance = 0.95;
    private const double Gib = 1024.0 * 1024 * 1024;

    public static CanRunReport Compare(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        var items = new List<CanRunItem>
        {
            Os(min, rec),
            Cpu(min, rec, pc), Gpu(min, rec, pc), Vram(min, rec, pc), Ram(min, rec, pc), Storage(min, rec, pc), Api(min, rec, pc),
        }.Where(i => i.Minimum.Length > 0 || i.Recommended.Length > 0).ToList();
        // A recommended list that says nothing about an item (e.g. only the minimum gives the disk space) does not ask for more than the minimum.
        if (!rec.IsEmpty) items = items.Select(i => i.Counts && i.Status == ReqStatus.MeetsMin && i.Recommended.Length == 0 ? i with { Status = ReqStatus.MeetsRec } : i).ToList();
        var counted = items.Where(i => i.Counts).ToList();
        var below = counted.Where(i => i.Status == ReqStatus.Below).Select(i => i.Title).ToList();
        var nearMin = counted.Where(i => i.Status == ReqStatus.NearMin).Select(i => i.Title).ToList();
        var nearRec = counted.Where(i => i.Status == ReqStatus.NearRec).Select(i => i.Title).ToList();
        var known = counted.Where(i => i.Status != ReqStatus.Unknown).ToList();
        int notCompared = counted.Count(i => i.Status == ReqStatus.Unknown);
        CanRunVerdict v = below.Count > 0 ? CanRunVerdict.BelowMinimum
            : known.Count < 2 ? CanRunVerdict.NoData
            : known.All(i => i.Status == ReqStatus.MeetsRec) ? CanRunVerdict.Recommended : CanRunVerdict.Minimum;
        string head = v switch
        {
            CanRunVerdict.BelowMinimum => L.T($"Abaixo do mínimo em: {string.Join(", ", below)}"),
            CanRunVerdict.Recommended => L.T("Roda no recomendado"),
            CanRunVerdict.Minimum when nearMin.Count > 0 => L.T($"Roda no mínimo, no limite em: {string.Join(", ", nearMin)}"),
            CanRunVerdict.Minimum when nearRec.Count > 0 => L.T($"Roda no mínimo; no limite do recomendado em: {string.Join(", ", nearRec)}"),
            CanRunVerdict.Minimum => L.T("Roda no mínimo"),
            _ => L.T("Sem dados suficientes"),
        };
        return new(items, v, below, notCompared, head, nearMin.Concat(nearRec).ToList());
    }

    private enum Fit { Unsure, Below, Near, Meets }
    private static Fit Of(bool? ok) => ok switch { true => Fit.Meets, false => Fit.Below, _ => Fit.Unsure };

    /// <summary>Combines the verdicts on the minimum and on the recommended list. A recommended list alone that the PC does not reach leaves the minimum unknown.</summary>
    private static ReqStatus Merge(Fit min, Fit rec)
    {
        if (rec == Fit.Meets) return ReqStatus.MeetsRec;
        if (min == Fit.Below) return ReqStatus.Below;
        if (rec == Fit.Near) return min == Fit.Near ? ReqStatus.NearMin : ReqStatus.NearRec;
        return min switch { Fit.Meets => ReqStatus.MeetsMin, Fit.Near => ReqStatus.NearMin, _ => ReqStatus.Unknown };
    }

    private static string Gb(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + " GB";

    private static CanRunItem Os(GameRequirement min, GameRequirement rec) =>
        new("os", L.T("Sistema"), min.Os ?? "", rec.Os ?? "", "Linux", ReqStatus.Unknown,
            (min.Os ?? rec.Os) is { } os && os.Contains("Linux", StringComparison.OrdinalIgnoreCase) && !os.Contains("Windows", StringComparison.OrdinalIgnoreCase)
                ? L.T("Versão para Linux publicada pelo desenvolvedor.") : L.T("Pede Windows: no Linux o jogo roda pelo Proton (Steam Play), sem garantia."), false);

    // --- indexed parts (CPU, GPU): relative gaming performance, RTX 4070 / Ryzen 7 7700X = 100 -----------------------------------------------
    // Local and requirement are rated by GpuIndexer/CpuIndexer: catalog anchor (model) > specification formula > family estimate.

    private static string Join(IReadOnlyList<string> alts) => string.Join(" / ", alts);

    private readonly record struct Judged(Fit Result, RatingBasis? Basis, bool Cap, double Ratio = 0);

    /// <summary>Below this ratio (PC / requirement) the PC is "below"; between it and 1.0 it is "at the limit". The weaker the data, the wider the band.</summary>
    internal static double BelowMargin(RatingBasis b) => b switch { RatingBasis.Model => 0.85, RatingBasis.Spec => 0.70, _ => 0.6 };
    /// <summary>Unknown model of an old family: the PC must be at least this many times the family's ceiling to count as "meets".</summary>
    internal const double OldFamilyMargin = 2.0;

    private static Judged Judge(Rating? local, IReadOnlyList<string> alternatives, Func<string, Rating?> rate)
    {
        if (alternatives.Count == 0 || local is null) return new(Fit.Unsure, null, false);
        bool unrated = false, allBelow = true, cap = false; RatingBasis? best = null, worst = null, nearBasis = null; double nearRatio = 0;
        foreach (var alt in alternatives)
        {
            var r = rate(alt);
            if (r is null) { unrated = true; allBelow = false; continue; }
            var basis = (RatingBasis)Math.Min((int)r.Basis, (int)local.Basis);
            worst = worst is null ? basis : (RatingBasis)Math.Min((int)worst, (int)basis);
            double ratio = local.Index / r.Index;
            if (r.OldFamilyCap) { allBelow = false; if (ratio >= OldFamilyMargin) { best = basis; cap = true; } continue; }
            if (ratio >= 1) { if (best is null || basis > best || cap) { best = basis; cap = false; } continue; }
            if (ratio >= BelowMargin(basis)) { allBelow = false; if (ratio > nearRatio) { nearRatio = ratio; nearBasis = basis; } }
        }
        if (best is not null) return new(Fit.Meets, best, cap);
        if (nearBasis is not null) return new(Fit.Near, nearBasis, false, nearRatio);
        return allBelow && !unrated ? new(Fit.Below, worst, false) : new(Fit.Unsure, null, false);
    }

    public static string BasisLabel(RatingBasis b) => b switch
    {
        RatingBasis.Model => L.T("por modelo"), RatingBasis.Spec => L.T("por especificação"), _ => L.T("estimado por família"),
    };

    private static CanRunItem Indexed(string key, string title, GameRequirement min, GameRequirement rec, Func<GameRequirement, IReadOnlyList<string>> sel, string localName,
        Rating? local, Func<string, Rating?> rate, string unknownLocal)
    {
        var jm = Judge(local, sel(min), rate); var jr = Judge(local, sel(rec), rate);
        var status = Merge(jm.Result, jr.Result);
        var decisive = status is ReqStatus.MeetsRec or ReqStatus.NearRec ? jr : jm;
        RatingBasis? basis = status == ReqStatus.Unknown ? null : decisive.Basis;
        string basisNote = basis switch
        {
            RatingBasis.Model => L.T("Comparado pelo desempenho relativo em jogos (tabela aproximada do Ilmenyx), não por clock ou núcleos."),
            RatingBasis.Spec => L.T("Comparado pela especificação (unidades de processamento e clock, ajustados pela arquitetura) ou pelo clock que o jogo informa; é uma estimativa."),
            _ => L.T("Estimado pela família e geração do modelo, sem o modelo exato; é uma estimativa."),
        };
        int pct = (int)Math.Round((1 - decisive.Ratio) * 100);
        string note = local is null ? unknownLocal
            : status == ReqStatus.Unknown ? ((sel(min).Count > 0 || sel(rec).Count > 0) ? L.T("Algum modelo pedido não está nas tabelas do Ilmenyx: não deu para comparar.") : "")
            : status == ReqStatus.NearMin ? L.T($"A diferença para o mínimo pedido é pequena (±{pct}%): deve rodar no mínimo, com ajustes baixos. ") + basisNote
            : status == ReqStatus.NearRec ? L.T($"A diferença para o recomendado é pequena (±{pct}%): o mínimo é atendido; no recomendado pode ser preciso baixar alguns ajustes. ") + basisNote
            : decisive.Cap ? L.T("O modelo pedido não está nas tabelas, mas é de uma família bem antiga e este PC é de uma geração muito mais nova: atende (estimado).")
            : basisNote;
        return new(key, title, Join(sel(min)), Join(sel(rec)), localName, status, note, true, basis);
    }

    private static CanRunItem Cpu(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        var local = pc.CpuName.Length == 0 ? null : CpuIndexer.Rate(pc.CpuName);
        return Indexed("cpu", L.T("Processador"), min, rec, g => g.CpuAlternatives, pc.CpuName.Length == 0 ? "—" : pc.CpuName, local, CpuIndexer.Rate,
            L.T("O processador deste PC não está nas tabelas do Ilmenyx: não deu para comparar."));
    }

    private static CanRunItem Gpu(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        var local = pc.GpuName.Length == 0 && pc.GpuPciId is null ? null : GpuIndexer.Rate(pc.GpuName, pc.GpuPciId);
        return Indexed("gpu", L.T("Placa de vídeo"), min, rec, g => g.GpuAlternatives, pc.GpuName.Length == 0 ? "—" : GpuLabel(pc.GpuName), local, a => GpuIndexer.Rate(a, null, requirement: true),
            L.T("A placa de vídeo deste PC não está nas tabelas do Ilmenyx (ou o nome é genérico e o ID PCI não identifica o modelo): não deu para comparar."));
    }

    internal static string GpuLabel(string raw)
    {
        int a = raw.IndexOf('['), b = raw.LastIndexOf(']');
        return a >= 0 && b > a + 1 ? raw[(a + 1)..b] : raw;
    }

    // --- numbers ------------------------------------------------------------------------------------------------------------------------

    private static CanRunItem Ram(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        double? have = pc.RamBytes > 0 ? pc.RamBytes / Gib : null;
        Fit Ok(double? need) => need is null || have is null ? Fit.Unsure : Of(have >= need * RamTolerance);
        return new("ram", L.T("Memória RAM"), min.RamGb is { } a ? Gb(a) : "", rec.RamGb is { } b ? Gb(b) : "", have is { } h ? Gb(Math.Round(h, 1)) : "—",
            Merge(Ok(min.RamGb), Ok(rec.RamGb)),
            L.T("A RAM lida pelo sistema é um pouco menor que a instalada (reserva do firmware); o Ilmenyx aceita 90% do valor pedido."), true);
    }

    /// <summary>Share of the system RAM an integrated GPU is assumed to be able to use as video memory (conservative; the driver usually allows about half).</summary>
    public const double SharedVramFraction = 0.5;
    /// <summary>Shared memory at least this fraction of the requested size is "at the limit"; below that the comparison is not made.</summary>
    public const double SharedNearFraction = 0.6;

    private static CanRunItem Vram(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        string req(double? v) => v is { } x ? Gb(x) : "";
        if (pc.GpuIntegrated)
        {
            // Integrated GPUs use the system RAM and the sysfs figure is not their usable VRAM: the estimate comes from the RAM, never from that figure.
            double? est = pc.RamBytes > 0 ? pc.RamBytes / Gib * SharedVramFraction : null;
            Fit Sh(double? need) => need is null || est is null ? Fit.Unsure : est >= need ? Fit.Meets : est >= need * SharedNearFraction ? Fit.Near : Fit.Unsure;
            string local = est is { } e ? L.T($"Compartilhada com a RAM (~{Gb(Math.Round(e, 1))} estimados)") : L.T("Compartilhada com a RAM");
            return new("vram", L.T("Memória de vídeo (VRAM)"), req(min.VramGb), req(rec.VramGb), local, Merge(Sh(min.VramGb), Sh(rec.VramGb)),
                L.T("A placa integrada usa a RAM do sistema como memória de vídeo. O Ilmenyx estima metade da RAM instalada (o que o driver costuma liberar) e nunca declara \"abaixo\" por isso. Memória compartilhada é mais lenta que VRAM dedicada: o jogo pode rodar pior do que o número sugere."),
                true, null, true);
        }
        double? have = pc.GpuVramBytes > 0 ? pc.GpuVramBytes / Gib : null;
        Fit Ok(double? need) => need is null || have is null ? Fit.Unsure : Of(have >= need * VramTolerance);
        return new("vram", L.T("Memória de vídeo (VRAM)"), req(min.VramGb), req(rec.VramGb),
            have is { } h ? Gb(Math.Round(h, 1)) : "—", Merge(Ok(min.VramGb), Ok(rec.VramGb)),
            have is null ? L.T("A VRAM não foi lida (driver que não informa): o Ilmenyx não deduz esse valor.")
                : L.T("A VRAM lida é um pouco menor que a nominal; o Ilmenyx aceita 95% do valor pedido."), true);
    }

    private static CanRunItem Storage(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        double? have = pc.FreeBytes is { } f ? f / Gib : null;
        Fit Ok(double? need) => need is null || have is null ? Fit.Unsure : Of(have >= need);
        return new("storage", L.T("Armazenamento"), min.StorageGb is { } a ? Gb(a) : "", rec.StorageGb is { } b ? Gb(b) : "",
            have is { } h ? L.T($"{Gb(Math.Round(h, 1))} livres em {pc.FreeLabel}") : "—", Merge(Ok(min.StorageGb), Ok(rec.StorageGb)),
            L.T("Conta o espaço livre do disco da sua pasta pessoal; se o jogo for instalado em outro disco, confira o espaço dele."), true);
    }

    // --- graphics API ---------------------------------------------------------------------------------------------------------------------

    private static CanRunItem Api(GameRequirement min, GameRequirement rec, LocalPc pc)
    {
        string Text(GameRequirement g) => string.Join(" · ", new[]
        {
            g.DirectX is { } d ? $"DirectX {d}" : null, g.Vulkan is { } v ? (v == "?" ? "Vulkan" : $"Vulkan {v}") : null, g.OpenGl is { } o ? $"OpenGL {o}" : null,
        }.Where(x => x is not null));
        int dx = Math.Max(min.DirectX ?? 0, rec.DirectX ?? 0);
        string note = dx >= 12 ? L.T("DirectX 12 roda no Linux pelo VKD3D-Proton, que exige uma GPU e um driver com Vulkan 1.3.")
            : dx > 0 ? L.T("DirectX 9 a 11 rodam no Linux pelo DXVK, que exige Vulkan (1.3 nas versões atuais) no driver.")
            : L.T("Esta exigência é de uma API gráfica nativa do Linux.");
        note += " " + (pc.VulkanLoader ? L.T("A biblioteca Vulkan está instalada neste PC; a versão que o driver anuncia não foi lida aqui (veja a página GPU).")
            : L.T("Não encontrei a biblioteca Vulkan neste PC; sem ela os jogos Windows não rodam pelo Proton."));
        return new("api", L.T("API gráfica"), Text(min), Text(rec), pc.VulkanLoader ? L.T("Vulkan instalado") : "—", ReqStatus.Unknown, note, false);
    }
}
