// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Adapted from RESET EXPLODE DiskBench.cs result model (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
namespace Ilmenyx.Core.Benchmarks;

public sealed record DiskBenchOptions(string Directory, int FileMegabytes = 1024, int RandomSeconds = 4, TimeSpan? Timeout = null)
{
    public const int MinimumFileMegabytes = 64, MaximumFileMegabytes = 8192;
    public const long FreeSpaceMarginBytes = 512L << 20;
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromMinutes(3);

    /// <summary>Null when the options are acceptable; otherwise why the run must not start.</summary>
    public static string? Validate(DiskBenchOptions o, long freeBytes)
    {
        if (string.IsNullOrWhiteSpace(o.Directory) || !Path.IsPathRooted(o.Directory)) return "Escolha uma pasta absoluta autorizada para o arquivo temporário.";
        if (o.FileMegabytes is < MinimumFileMegabytes or > MaximumFileMegabytes) return $"Tamanho do arquivo de {MinimumFileMegabytes} a {MaximumFileMegabytes} MB.";
        if (o.RandomSeconds is < 1 or > 10) return "Duração das fases aleatórias de 1 a 10 s.";
        if (o.EffectiveTimeout <= TimeSpan.Zero || o.EffectiveTimeout > TimeSpan.FromMinutes(10)) return "Timeout de até 10 minutos.";
        string full = Path.GetFullPath(o.Directory);
        foreach (string forbidden in new[] { "/dev", "/proc", "/sys", "/run/udev" })
            if (full == forbidden || full.StartsWith(forbidden + "/", StringComparison.Ordinal)) return $"{forbidden} não é uma pasta de dados: o teste nunca usa dispositivos.";
        long need = ((long)o.FileMegabytes << 20) + FreeSpaceMarginBytes;
        if (freeBytes < need) return $"Pouco espaço livre: precisa de {need >> 20} MB e há {freeBytes >> 20} MB.";
        return null;
    }
}

/// <summary>Direct-I/O storage benchmark result. Numbers are null unless the whole run completed.</summary>
public sealed record DiskBenchResult(BenchRunStatus Status, string Directory, int FileMegabytes, double? SeqReadMBs, double? SeqWriteMBs,
    double? ReadIopsQ1, double? WriteIopsQ1, double? ReadLatencyUs, double? WriteLatencyUs, double? ReadIopsQ8, string? Reason)
{
    /// <summary>History bar only (original formula): sequential weighs copy/load, 4K weighs opening programs.</summary>
    public double? Score => Status == BenchRunStatus.Completed
        ? (SeqReadMBs!.Value + SeqWriteMBs!.Value) / 2 + (ReadIopsQ1!.Value + WriteIopsQ1!.Value) / 200.0 + ReadIopsQ8!.Value / 2000.0
        : null;
}
