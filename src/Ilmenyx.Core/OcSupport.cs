// Adapted from RESET EXPLODE MainWindow.Oc.cs / OcCheck.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public sealed record OcGuideStep(string Number, string Title, string Where, string Text);

/// <summary>BIOS overclock guide. Menu paths are starting points that vary with the BIOS version; nothing here changes the system.</summary>
public static class OcGuideBuilder
{
    public static (string Title, string Intro, IReadOnlyList<OcGuideStep> Steps) Build(string boardVendor, string boardName, string cpuName)
    {
        bool gigabyte = boardVendor.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase);
        bool asus = boardVendor.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
        bool msi = boardVendor.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || boardVendor.Contains("MSI", StringComparison.OrdinalIgnoreCase);
        string shortCpu = GameCatalog.ShortCpuName(cpuName);
        bool amd = cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        string board = ((gigabyte ? "Gigabyte" : boardVendor.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "") + " " + boardName).Trim();
        string pbo = gigabyte ? "Tweaker → Advanced CPU Settings → Precision Boost Overdrive  (ou Settings → AMD Overclocking)"
            : asus ? "Advanced → AMD Overclocking → Precision Boost Overdrive  (ou AI Tweaker → PBO)"
            : msi ? "OC → Advanced CPU Configuration → AMD Overclocking → Precision Boost Overdrive"
            : "Advanced → AMD Overclocking → Precision Boost Overdrive";
        string expo = gigabyte ? "Tweaker → Extreme Memory Profile (X.M.P.) → EXPO1"
            : asus ? "AI Tweaker → Ai Overclock Tuner → EXPO I"
            : msi ? "OC → A-XMP/EXPO → Profile 1"
            : "procure EXPO / XMP no menu de memória";
        var steps = new List<OcGuideStep>
        {
            new("0", "Antes de começar", gigabyte ? "Save & Exit → Save Profiles" : "Save & Exit → Save Profile", "Salve o perfil atual da BIOS. Se o PC não ligar depois de um ajuste, espere 3 tentativas (muitas placas voltam sozinhas) ou limpe a CMOS (jumper CLR_CMOS na placa ou tirando a bateria por 1 minuto)."),
            new("1", "Ative o perfil da memória (EXPO)", expo, "É o maior ganho em jogos para a maioria dos PCs: a RAM sai do padrão lento e vai para a velocidade da etiqueta. Teste: verificador \"CPU + RAM\" por 15 min (o teste de RAM acusa erro se o perfil não for estável).")
        };
        if (amd)
        {
            steps.Add(new("2", "Precision Boost Overdrive (PBO)", pbo, "Precision Boost Overdrive → Advanced. PBO Limits → Motherboard. Isso libera os limites de energia para o boost durar mais. Teste com o verificador."));
            steps.Add(new("3", "Curve Optimizer (undervolt da CPU)", pbo + " → Curve Optimizer", "All Cores → Negative → 10. Teste. Se passar, tente 15, 20, 25… até falhar; volte 5 a partir de onde falhou. No " + shortCpu + " costuma ficar entre -15 e -30. Instabilidade de Curve Optimizer aparece muito em uso leve: use o PC normalmente por um dia e veja \"Erros de hardware recentes\" aqui — erro de hardware no log do kernel (MCE) = volte um passo."));
            steps.Add(new("4", "Max CPU Boost Clock Override (opcional)", pbo, "Depois do Curve Optimizer estável: +100 MHz, teste; depois +200 MHz. Ganho pequeno; pode exigir reduzir o Curve Optimizer."));
            steps.Add(new("5", "Alternativa fria: Eco Mode", pbo + " → Eco Mode", "Em vez de mais desempenho: limita a CPU a 65 W (ou 105 W). Perde ~5–10% em tarefas pesadas multi-core, quase nada em jogos, e roda bem mais frio e silencioso."));
        }
        else
            steps.Add(new("2", "Processador Intel", "Tweaker / Ai Tweaker / OC → CPU", "Para Intel, prefira undervolt (offset negativo de tensão) e limites de energia (PL1/PL2) em vez de aumentar multiplicadores. Teste cada passo com o verificador."));
        return ("GUIA: OVERCLOCK NA BIOS · " + board.ToUpperInvariant() + " · " + shortCpu.ToUpperInvariant(),
            "Faça uma mudança por vez e rode o verificador depois de cada uma. Os menus podem mudar um pouco com a versão da BIOS. Valores são pontos de partida seguros, não garantia: cada chip é diferente. O Ilmenyx não altera a BIOS.", steps);
    }
}

public sealed record KernelHardwareErrors(int Cpu, int Memory, int Pcie, int Gpu, string? Last, string? Error)
{
    public int Total => Cpu + Memory + Pcie + Gpu;
    public static KernelHardwareErrors Unavailable(string reason) => new(0, 0, 0, 0, null, reason);
}

/// <summary>Counts hardware-error and GPU-reset lines in kernel log text (journalctl -k / dmesg). Pattern matching only; it proves nothing about the cause.</summary>
public static class KernelLogParser
{
    private static readonly Regex CpuRx = new(@"\bmce:|machine check|hardware error|\[Hardware Error\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MemRx = new(@"\bEDAC\b.*\b(error|CE|UE)\b|memory (corrected|uncorrected) error", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PcieRx = new(@"\bAER:|PCIe Bus Error", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GpuRx = new(@"NVRM: Xid|amdgpu.*(GPU reset|ring .* timeout)|i915.*(GPU HANG|GPU hang)|\bxe\b.*(GT reset|GPU hang)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public const int MaxLines = 20000, MaxLastCharacters = 160;

    public static KernelHardwareErrors Parse(string text)
    {
        int cpu = 0, mem = 0, pcie = 0, gpu = 0; string? last = null; int lines = 0;
        foreach (string line in text.Split('\n'))
        {
            if (++lines > MaxLines) break;
            bool hit = false;
            if (CpuRx.IsMatch(line)) { cpu++; hit = true; }
            else if (MemRx.IsMatch(line)) { mem++; hit = true; }
            else if (PcieRx.IsMatch(line)) { pcie++; hit = true; }
            else if (GpuRx.IsMatch(line)) { gpu++; hit = true; }
            if (hit)
            {
                string clean = new(line.Where(c => !char.IsControl(c)).ToArray());
                last = clean.Length > MaxLastCharacters ? clean[..MaxLastCharacters] + "…" : clean;
            }
        }
        return new(cpu, mem, pcie, gpu, last, null);
    }
}

public sealed record OcCheckOptions(bool Cpu, TimeSpan Duration, string Label, double CpuTempLimit = 95)
{
    public static readonly TimeSpan[] AllowedDurations = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];
    public static string? Validate(OcCheckOptions o) =>
        !o.Cpu ? "Escolha CPU + RAM (o teste de GPU depende do módulo Vulkan, ainda não portado)."
        : !AllowedDurations.Contains(o.Duration) ? "Duração de 5, 15, 30 ou 60 minutos."
        : o.CpuTempLimit is < 60 or > 105 ? "Limite de temperatura de 60 a 105 °C."
        : o.Label.Length > 80 ? "Descrição de até 80 caracteres." : null;
}

public enum OcCheckStatus { Completed, Cancelled, Unavailable }

public sealed record OcCheckResult(OcCheckStatus Status, bool? Stable, string? FailureReason, double? CpuScore, double? MaxCpuTemp,
    long MemoryPasses, long MemoryErrors, KernelHardwareErrors Errors, TimeSpan Elapsed, string? Note);

/// <summary>
/// Stability verifier (CPU + RAM stress with automatic cuts). Injectable clock/readings so the decision logic is unit-tested without any load.
/// Stable is published only when the whole duration ran without a cut; a cancelled run has no verdict.
/// </summary>
public static class OcCheckRunner
{
    public static async Task<OcCheckResult> RunAsync(OcCheckOptions options, IStressEngine engine, long availableMemoryBytes,
        Func<CancellationToken, Task<double?>> measureCpuScore, Func<double?> cpuTemp, Func<DateTimeOffset, Task<KernelHardwareErrors>> errorsSince,
        Func<TimeSpan, CancellationToken, Task> delay, Func<DateTimeOffset> now, CancellationToken ct, IProgress<(string Status, double Fraction)>? progress = null)
    {
        if (OcCheckOptions.Validate(options) is { } why)
            return new(OcCheckStatus.Unavailable, null, why, null, null, 0, 0, KernelHardwareErrors.Unavailable(why), TimeSpan.Zero, null);
        var start = now(); double? score = null; double? maxTemp = null; string? fail = null; bool cancelled = false;
        try
        {
            progress?.Report(("Medindo a nota da CPU (render multi-core)…", 0));
            score = await measureCpuScore(ct);
            ct.ThrowIfCancellationRequested();
            try { engine.Start(cpu: true, ram: true, 0.6, availableMemoryBytes); }
            catch (InvalidOperationException e) { return new(OcCheckStatus.Unavailable, null, e.Message, score, null, 0, 0, KernelHardwareErrors.Unavailable(e.Message), now() - start, null); }
            var stressStart = now(); int ticks = 0;
            while (now() - stressStart < options.Duration)
            {
                await delay(TimeSpan.FromSeconds(1), ct); ct.ThrowIfCancellationRequested(); ticks++;
                if (cpuTemp() is double t) maxTemp = Math.Max(maxTemp ?? t, t);
                var elapsed = now() - stressStart;
                progress?.Report((string.Create(CultureInfo.InvariantCulture, $"Stress CPU + RAM · {elapsed:mm\\:ss} de {options.Duration:mm\\:ss} · máx. CPU {(maxTemp is double m ? m.ToString("0", CultureInfo.InvariantCulture) + " °C" : "—")}"), Math.Min(1, elapsed / options.Duration)));
                if (cpuTemp() is double now1 && now1 >= options.CpuTempLimit) { fail = string.Create(CultureInfo.InvariantCulture, $"CPU chegou a {now1:0} °C (limite de segurança {options.CpuTempLimit:0} °C)"); break; }
                if (engine.MemoryErrors > 0) { fail = $"{engine.MemoryErrors} erros de memória (RAM/EXPO instável)"; break; }
                if (ticks % 5 == 0 && (await errorsSince(start)) is { Total: > 0 } e) { fail = Describe(e); break; }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        long passes = engine.MemoryPasses, errors = engine.MemoryErrors;
        engine.Stop();
        var final = await SafeErrors(errorsSince, start);
        var elapsedTotal = now() - start;
        if (cancelled) return new(OcCheckStatus.Cancelled, null, null, null, maxTemp, passes, errors, final, elapsedTotal, "Verificação cancelada: nenhum veredito é publicado.");
        if (fail is null && final.Total > 0) fail = Describe(final);
        string? note = final.Error is not null ? "Log do kernel indisponível (" + final.Error + "): erros de hardware não foram verificados; o veredito considera só temperatura e erros de memória." : null;
        return new(OcCheckStatus.Completed, fail is null, fail, score, maxTemp, passes, errors, final, elapsedTotal, note);
    }

    private static async Task<KernelHardwareErrors> SafeErrors(Func<DateTimeOffset, Task<KernelHardwareErrors>> read, DateTimeOffset since)
    {
        try { return await read(since); }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or InvalidOperationException) { return KernelHardwareErrors.Unavailable(e.Message); }
    }

    private static string Describe(KernelHardwareErrors e) =>
        $"o kernel registrou erros de hardware (CPU/MCE {e.Cpu}, memória/EDAC {e.Memory}, PCIe {e.Pcie}) e {e.Gpu} reinícios/travamentos de GPU";
}

public sealed record NvidiaTuningState(int Index, string Name, double? PowerLimitW, double? PowerDefaultW, double? PowerMinW, double? PowerMaxW, uint? MaxClockMhz, uint? CurrentClockMhz);

/// <summary>NVIDIA power/clock tuning. Ilmenyx never runs nvidia-smi with privileges: it validates the request and prints the commands for the user.</summary>
public static class NvidiaTuning
{
    public const string QueryFields = "index,name,power.limit,power.default_limit,power.min_limit,power.max_limit,clocks.max.graphics,clocks.current.graphics";

    public static IReadOnlyList<NvidiaTuningState> Parse(string csv)
    {
        var list = new List<NvidiaTuningState>();
        double? D(string s) => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 ? v : null;
        uint? U(string s) => uint.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint v) ? v : null;
        foreach (string line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(',');
            if (f.Length < 8 || !int.TryParse(f[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index)) continue;
            list.Add(new(index, f[1].Trim(), D(f[2]), D(f[3]), D(f[4]), D(f[5]), U(f[6]), U(f[7])));
        }
        return list;
    }

    /// <summary>Commands for the requested limits; throws ArgumentOutOfRangeException when a value is outside what the card reports.</summary>
    public static IReadOnlyList<string> Commands(NvidiaTuningState gpu, double powerWatts, uint? clockCapMhz)
    {
        if (gpu.PowerMinW is not double min || gpu.PowerMaxW is not double max) throw new InvalidOperationException("A placa não informa a faixa do limite de energia.");
        if (powerWatts < min || powerWatts > max) throw new ArgumentOutOfRangeException(nameof(powerWatts), $"Limite de energia de {min:0} a {max:0} W.");
        if (clockCapMhz is { } cap && (cap < 300 || cap > (gpu.MaxClockMhz ?? 5000))) throw new ArgumentOutOfRangeException(nameof(clockCapMhz), "Teto de clock fora da faixa da placa.");
        string i = gpu.Index.ToString(CultureInfo.InvariantCulture);
        return [$"sudo nvidia-smi -i {i} -pl {powerWatts.ToString("0", CultureInfo.InvariantCulture)}",
            clockCapMhz is { } c ? $"sudo nvidia-smi -i {i} -lgc 210,{c.ToString(CultureInfo.InvariantCulture)}" : $"sudo nvidia-smi -i {i} -rgc"];
    }

    public static IReadOnlyList<string> ResetCommands(NvidiaTuningState gpu)
    {
        string i = gpu.Index.ToString(CultureInfo.InvariantCulture);
        var list = new List<string>();
        if (gpu.PowerDefaultW is double d) list.Add($"sudo nvidia-smi -i {i} -pl {d.ToString("0", CultureInfo.InvariantCulture)}");
        list.Add($"sudo nvidia-smi -i {i} -rgc");
        return list;
    }
}
