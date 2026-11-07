// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Cli;

/// <summary>
/// `ilmenyx nvidia-driver status|plan`: read-only. `status` prints the diagnosis; `plan` also prints each action's steps, risks and fixed
/// script and runs the real `apt-get -s` dry run (no root, nothing changes). It never installs, removes or calls pkexec.
/// </summary>
public static class NvidiaDriverCommand
{
    public static bool Handles(string[] args) => args is ["nvidia-driver", ..];
    private const string Usage = "Uso: ilmenyx nvidia-driver status | plan   (somente leitura; nada é instalado)";

    public static async Task<int> RunAsync(string[] args, TextWriter o, CancellationToken ct, NvidiaDriverProbe? probe = null)
    {
        if (args is not ["nvidia-driver", "status" or "plan"]) { await o.WriteLineAsync(Usage); return 2; }
        probe ??= new NvidiaDriverProbe();
        var d = await probe.DiagnoseAsync(ct);
        var plan = NvidiaPlanner.Plan(d);
        await o.WriteLineAsync($"Driver NVIDIA · {plan.Headline}");
        await o.WriteLineAsync(plan.Summary);
        await o.WriteLineAsync("Estado: " + plan.State);
        foreach (var f in plan.Findings) await o.WriteLineAsync("  · " + f);
        if (plan.Guidance is { } g) await o.WriteLineAsync("Orientação: " + g);
        if (args[1] == "status")
        {
            await o.WriteLineAsync("Ações possíveis: " + (plan.Actions.Count == 0 ? "nenhuma" : string.Join(", ", plan.Actions.Select(a => a.Button))) + "  (detalhes: ilmenyx nvidia-driver plan)");
            return 0;
        }
        if (plan.Actions.Count == 0) await o.WriteLineAsync("Nenhuma ação a propor.");
        foreach (var a in plan.Actions)
        {
            await o.WriteLineAsync();
            await o.WriteLineAsync($"== {a.Button}: {a.Title}{(a.Tested ? "" : "  [NÃO TESTADO]")}");
            if (a.Blocked is { } b) { await o.WriteLineAsync("Sem execução por clique: " + b); continue; }
            foreach (var s in a.Steps) await o.WriteLineAsync("  - " + s);
            foreach (var r in a.Risks) await o.WriteLineAsync("  ! " + r);
            if (a.SimulationArguments is { } sim)
            {
                await o.WriteLineAsync("Simulação real: apt-get -s " + string.Join(' ', sim));
                try
                {
                    var result = await probe.SimulateAsync(a, ct);
                    foreach (var x in result.Actions.Where(x => x.Kind != "Conf")) await o.WriteLineAsync($"    {x.Kind} {x.Package} {x.Version}");
                    await o.WriteLineAsync("    " + (result.Summary.Length > 0 ? result.Summary : "(sem resumo)"));
                    var problems = NvidiaPlanner.CheckSimulation(a, result, d.RunningKernel);
                    await o.WriteLineAsync(problems.Count == 0 ? "  Simulação aceita: nenhum kernel instalado ou removido." : "  PLANO RECUSADO:\n" + string.Join('\n', problems.Select(p => "    " + p)));
                }
                catch (Exception e) when (e is FileNotFoundException or IOException or TimeoutException or System.ComponentModel.Win32Exception) { await o.WriteLineAsync("  Simulação indisponível: " + e.Message); }
            }
            else await o.WriteLineAsync("Sem simulação nesta distribuição.");
            await o.WriteLineAsync("Script fixo (rodaria por pkexec só após clique e confirmação na interface):");
            foreach (var l in a.Script.TrimEnd('\n').Split('\n')) await o.WriteLineAsync("    " + l);
        }
        return 0;
    }
}
