// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from the dashboard summaries of RESET EXPLODE MainWindow.Dash.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Globalization;

namespace Ilmenyx.Core;

public sealed record PerformanceSummary(double? CpuScore, double? CpuDelta, string CpuSubtitle, IReadOnlyList<KeyValuePair<string, string>> Others)
{
    /// <summary>Latest CPU render score with the change from the previous one, plus the latest result of each other test. Scores are experimental (not comparable with Windows).</summary>
    public static PerformanceSummary From(IReadOnlyList<BenchRunEntry> runs)
    {
        var cpu = runs.Where(r => r.Kind == "CPU" && r.Score is not null).ToList();
        double? delta = cpu.Count > 1 ? cpu[0].Score - cpu[1].Score : null;
        string sub = cpu.Count > 0 ? L.T($"pts · render de CPU · {cpu[0].At.LocalDateTime.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}") : L.T("rode o Benchmark para ter uma nota");
        string Last(string kind) => runs.FirstOrDefault(r => r.Kind == kind)?.Summary.Split('·')[0].Trim() ?? "—";
        return new(cpu.Count > 0 ? cpu[0].Score : null, delta, sub,
        [
            new(L.T("Memória"), Last(L.T("Memória"))), new(L.T("Simulação de jogo"), Last("GameSim")), new(L.T("Disco"), Last(L.T("Disco"))),
            new(L.T("Verificação OC"), runs.FirstOrDefault(r => r.Kind == "OC") is { } oc ? oc.Summary.Split('·')[0].Trim() : "—")
        ]);
    }
}

public sealed record HealthCheck(string Title, string Detail, bool? Ok);

/// <summary>Quick PC health checklist from readings the app already has. Unknown (not Ok, not bad) is its own state so a missing source is never shown as healthy.</summary>
public static class HealthChecklist
{
    public sealed record Inputs(bool? CpuTempAvailable, KernelHardwareErrors? KernelErrors, int DisksGood, int DisksAttention, int DisksUnknown,
        string? PowerProfile, bool OnBattery, bool? GameModeInstalled);

    public static IReadOnlyList<HealthCheck> Build(Inputs i)
    {
        var list = new List<HealthCheck>();
        if (i.CpuTempAvailable is bool t)
            list.Add(new(L.T("Sensor de temperatura da CPU"), t ? L.T("Temperatura da CPU disponível.") : L.T("Nenhum sensor de temperatura da CPU foi encontrado (hwmon coretemp/k10temp); o stress test não terá corte por temperatura."), t));
        list.Add(i.KernelErrors switch
        {
            null => new(L.T("Erros de hardware"), L.T("Ainda não verificado."), null),
            { Error: not null } e => new(L.T("Erros de hardware"), L.T("Não foi possível ler o log do kernel: ") + e.Error + ".", null),
            { Total: 0 } => new(L.T("Sem erros de hardware"), L.T("Nenhum MCE, EDAC, PCIe AER ou reinício de GPU no log do kernel em 7 dias."), true),
            var e => new(L.T("Erros de hardware"), L.T($"{e.Total} ocorrência(s) em 7 dias (CPU/MCE {e.Cpu}, memória {e.Memory}, PCIe {e.Pcie}, GPU {e.Gpu}). Veja a página OC."), false)
        });
        int disks = i.DisksGood + i.DisksAttention + i.DisksUnknown;
        if (disks > 0)
            list.Add(i.DisksAttention > 0 ? new(L.T("Discos"), L.T($"{i.DisksAttention} disco(s) com aviso. Veja a página Discos e faça backup."), false)
                : i.DisksUnknown > 0 ? new(L.T("Discos"), L.T($"{i.DisksUnknown} de {disks} disco(s) sem dados de saúde (SMART não consultado)."), null)
                : new(L.T("Discos"), L.T("Todos os discos informam saúde sem avisos."), true));
        if (i.PowerProfile is { Length: > 0 } p)
            list.Add(new(L.T("Perfil de energia"), i.OnBattery ? L.T($"Em bateria, perfil {p}.") : p == "performance" ? L.T("Alto desempenho ativo.") : L.T($"Perfil {p}: para jogar, o Launcher ativa Alto desempenho durante o jogo."), i.OnBattery ? null : true));
        if (i.GameModeInstalled is bool g)
            list.Add(new("GameMode", g ? L.T("gamemoderun encontrado: o Launcher pode usá-lo.") : L.T("gamemode não instalado (opcional, pacote da distribuição)."), g ? true : null));
        return list;
    }
}
