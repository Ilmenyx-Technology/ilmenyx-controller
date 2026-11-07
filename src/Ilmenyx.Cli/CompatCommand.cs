// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Globalization;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Cli;

/// <summary>
/// `ilmenyx compat`: game compatibility (ProtonDB, Steam Deck, anti-cheat). Offline unless --allow-network is given:
///   status · search TEXTO · game APPID [--online --allow-network] · update --allow-network · build-from ARQUIVO.tar.gz
/// </summary>
public static class CompatCommand
{
    public static bool Handles(string[] args) => args is ["compat", ..];
    private const string Usage = "Uso: ilmenyx compat status | search TEXTO | game APPID [--online --allow-network] | update --allow-network | build-from ARQUIVO.tar.gz";

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static async Task<int> RunAsync(string[] args, TextWriter o, CancellationToken ct)
    {
        var db = new CompatDatabase(CompatPaths.Folder());
        bool network = args.Contains("--allow-network");
        var rest = args.Skip(1).Where(a => a != "--allow-network" && a != "--online").ToArray();
        switch (rest)
        {
            case ["status"]:
                if (db.ReadMeta() is { } m) await o.WriteLineAsync($"Base local: {m.Source} · {m.Apps:N0} jogos · {m.Reports:N0} relatos de {m.Oldest:dd/MM/yyyy} a {m.Newest:dd/MM/yyyy} · montada em {m.BuiltAt.ToLocalTime():dd/MM/yyyy HH:mm}\n{CompatDatabase.Attribution}");
                else await o.WriteLineAsync("Base local de relatos ainda não baixada. Use: ilmenyx compat update --allow-network (~70 MB, do GitHub).");
                return 0;
            case ["search", .. var words] when words.Length > 0:
                foreach (var (id, title) in db.Search(string.Join(' ', words))) await o.WriteLineAsync($"{id,10}  {title}");
                return 0;
            case ["update"]:
                if (!network) { await o.WriteLineAsync("A atualização usa a rede (api.github.com e raw.githubusercontent.com, ~70 MB). Repita com --allow-network."); return 2; }
                try
                {
                    var meta = await new CompatOnline(CompatPaths.Folder()).UpdateDatabaseAsync(db, new Progress<string>(s => Console.Error.WriteLine(s)), ct, LinuxCompatHardware.Detect());
                    await o.WriteLineAsync($"Base pronta: {meta.Apps:N0} jogos, {meta.Reports:N0} relatos (até {meta.Newest:dd/MM/yyyy}).");
                    return 0;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or TaskCanceledException) { await o.WriteLineAsync("Falha: " + e.Message); return 1; }
            case ["build-from", var file]:
                await using (var s = File.OpenRead(file))
                {
                    var meta = await CompatDatabase.BuildAsync(s, db.Folder, new CompatDumpInfo(Path.GetFileName(file) + " (arquivo local, hash não conferido)", new FileInfo(file).Length, "", "", null), new Progress<int>(n => Console.Error.WriteLine($"{n:N0} relatos lidos")), ct, LinuxCompatHardware.Detect());
                    await o.WriteLineAsync($"Base pronta: {meta.Apps:N0} jogos, {meta.Reports:N0} relatos.");
                }
                return 0;
            case ["game", var app] when app.All(char.IsAsciiDigit) && app.Length is > 0 and <= 12:
                return await GameAsync(db, app, args.Contains("--online"), network, o, ct);
            default:
                await o.WriteLineAsync(Usage); return 2;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task<int> GameAsync(CompatDatabase db, string app, bool online, bool network, TextWriter o, CancellationToken ct)
    {
        await o.WriteLineAsync($"{db.Title(app) ?? "Jogo"} (Steam {app})");
        if (online && !network) { await o.WriteLineAsync("--online usa a rede (protondb.com, store.steampowered.com, raw.githubusercontent.com). Repita com --allow-network."); return 2; }
        if (online)
        {
            var net = new CompatOnline(CompatPaths.Folder());
            try
            {
                var s = await net.SummaryAsync(app, ct);
                await o.WriteLineAsync(s is null ? "ProtonDB: sem relatos." : $"ProtonDB: {ProtonSummary.TierName(s.Tier)} ({ProtonSummary.TierMeaning(s.Tier)}) · tendência {ProtonSummary.TierName(s.TrendingTier)} · {s.Total} relatos · confiança {ProtonSummary.ConfidenceName(s.Confidence)}");
                var d = await net.DeckAsync(app, ct);
                await o.WriteLineAsync($"Steam Deck: {d?.Name ?? "—"}" + (d is { Notes.Count: > 0 } ? " · " + string.Join("; ", d.Notes) : ""));
                var a = await net.AntiCheatAsync(app, ct);
                await o.WriteLineAsync(a is null ? "Anti-cheat: não listado no AreWeAntiCheatYet (geralmente jogo sem anti-cheat online)." : $"Anti-cheat: {a.StatusName} · {string.Join(", ", a.AntiCheats)}");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { await o.WriteLineAsync("Consulta online falhou: " + e.Message); }
        }
        var reports = db.Reports(app);
        if (reports.Count == 0) { await o.WriteLineAsync(db.ReadMeta() is null ? "Base local de relatos não baixada (ilmenyx compat update --allow-network)." : "Nenhum relato na base local."); return 0; }
        var hw = LinuxCompatHardware.Detect();
        await o.WriteLineAsync($"Sua máquina: {hw.GpuText} · {hw.Distro}");
        var ranked = CompatMatcher.RankAll(reports, hw, DateTimeOffset.UtcNow);
        var (options, protons) = CompatMatcher.Suggest(ranked);
        int works = reports.Count(r => r.Works);
        int same = reports.Count(r => CompatMatcher.SameGpu(hw, r.Gpu));
        await o.WriteLineAsync($"Relatos locais: {reports.Count} ({works} funcionando) · com a mesma GPU, série ou chip: {same}");
        if (options.Count > 0) await o.WriteLineAsync("Opções mais usadas por quem tem hardware parecido e conseguiu jogar: " + string.Join(" · ", options.Select(x => $"{x.Option} ({x.Count}/{x.Of})")));
        if (protons.Count > 0) await o.WriteLineAsync("Versões do Proton que funcionaram: " + string.Join(" · ", protons.Select(x => $"{x.Option} ({x.Count}/{x.Of})")));
        foreach (var x in ranked.Take(5))
        {
            var r = x.Report;
            await o.WriteLineAsync($"\n— {r.Date:dd/MM/yyyy} · {(r.Works ? "funciona" : "NÃO funciona")} · {r.Gpu} · {r.Os} · Proton {(r.Proton.Length > 0 ? r.Proton : "padrão")} · semelhança {x.Score} ({string.Join(", ", x.Matches)})");
            foreach (var f in r.Faults) await o.WriteLineAsync($"  Problema: {CompatReport.FaultName(f)}" + (r.FaultNotes.TryGetValue(f, out var n) ? " — " + n.Replace('\n', ' ') : ""));
            if (r.LaunchOptions is { } lo) await o.WriteLineAsync($"  Opções: {lo}  [{(LaunchOptionSafety.Check(lo) is { Applicable: true } ? "pode ser aplicada" : "só leitura: " + LaunchOptionSafety.Check(lo).Reason)}]");
            if (r.ConcludingNotes is { } cn) await o.WriteLineAsync("  Comentário: " + cn.Replace('\n', ' '));
        }
        await o.WriteLineAsync("\n" + CompatDatabase.Attribution + ". Relatos são de usuários anônimos: revise antes de aplicar.");
        return 0;
    }
}
