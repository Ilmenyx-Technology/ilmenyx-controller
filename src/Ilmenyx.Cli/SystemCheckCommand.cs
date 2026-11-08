// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Cli;

/// <summary>
/// `ilmenyx game-check` and `ilmenyx zram status|plan`: read-only text versions of the "Jogo não abre?" page and of the ZRAM card, for
/// headless checks (VMs, other distros). They never run a fix, pkexec or a package manager; `zram plan` only PRINTS the fixed scripts.
/// </summary>
public static class SystemCheckCommand
{
    public static bool Handles(string[] args) => args is ["game-check", ..] or ["zram", ..];
    private const string Usage = "Uso: ilmenyx game-check | ilmenyx zram status | ilmenyx zram plan | ilmenyx zram script apply|undo|undo-remove   (somente leitura; nada é alterado, o script só é impresso)";

    public static async Task<int> RunAsync(string[] args, TextWriter o, CancellationToken ct, GameLaunchProbe? games = null, ZramProbe? zram = null)
    {
        if (args is ["game-check"])
        {
            var facts = await (games ?? new GameLaunchProbe()).CollectAsync(ct);
            var checks = GameLaunchChecks.Build(facts);
            await o.WriteLineAsync("Jogo não abre? · " + (facts.Distro.Name.Length > 0 ? facts.Distro.Name : "sistema não identificado"));
            await o.WriteLineAsync(GameLaunchChecks.Summary(checks));
            foreach (var c in checks.OrderByDescending(c => c.Status))
            {
                await o.WriteLineAsync();
                await o.WriteLineAsync($"[{GameLaunchChecks.StatusText(c.Status)}] {c.Title}");
                await o.WriteLineAsync("  " + c.Found);
                if (c.Warning is { } w) await o.WriteLineAsync("  ! " + w);
                foreach (var (label, command) in c.Commands) await o.WriteLineAsync($"  $ {command}   # {label}");
                foreach (var f in c.Fixes) await o.WriteLineAsync($"  (botão na interface) flatpak {string.Join(' ', f.Arguments)}");
                if (c.LinkPage is { } p) await o.WriteLineAsync("  Veja também a página: " + p);
            }
            return 0;
        }
        if (args is ["zram", "script", var which])
        {
            // Prints only the fixed script of the current plan (for a disposable VM test); exit 1 when the plan has no such action.
            ZramActionKind? kind = which switch { "apply" => ZramActionKind.Apply, "undo" => ZramActionKind.Undo, "undo-remove" => ZramActionKind.UndoAndRemovePackage, _ => null };
            if (kind is null) { await o.WriteLineAsync(Usage); return 2; }
            var p = ZramPlanner.Plan(await (zram ?? new ZramProbe()).CollectAsync(ct));
            if (p.Actions.FirstOrDefault(a => a.Kind == kind) is not { } action) { await Console.Error.WriteLineAsync($"Sem a ação '{which}' no plano atual ({p.State}: {p.Headline})."); return 1; }
            await o.WriteAsync(action.Script);
            return 0;
        }
        if (args is not ["zram", "status" or "plan"]) { await o.WriteLineAsync(Usage); return 2; }
        var plan = ZramPlanner.Plan(await (zram ?? new ZramProbe()).CollectAsync(ct));
        await o.WriteLineAsync("ZRAM · " + plan.Headline);
        await o.WriteLineAsync(plan.Summary);
        await o.WriteLineAsync("Estado: " + plan.State + (plan.SizeMiB is { } s ? $" · tamanho proposto: {s} MiB" : ""));
        foreach (var f in plan.Findings) await o.WriteLineAsync("  · " + f);
        if (args[1] == "status")
        {
            await o.WriteLineAsync("Ações possíveis: " + (plan.Actions.Count == 0 ? "nenhuma" : string.Join(", ", plan.Actions.Select(a => a.Button))) + "  (scripts: ilmenyx zram plan)");
            return 0;
        }
        foreach (var a in plan.Actions)
        {
            await o.WriteLineAsync();
            await o.WriteLineAsync($"== {a.Button}: {a.Title}{(a.Tested ? "" : "  [NÃO TESTADO]")}");
            foreach (var st in a.Steps) await o.WriteLineAsync("  - " + st);
            foreach (var r in a.Risks) await o.WriteLineAsync("  ! " + r);
            await o.WriteLineAsync("-- script (root, via pkexec na interface):");
            await o.WriteAsync(a.Script);
        }
        return 0;
    }
}
