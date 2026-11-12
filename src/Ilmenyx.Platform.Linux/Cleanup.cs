// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public enum CleanupKind { Trash, UserCache, PackageCache, Journal, FlatpakUnused }

/// <param name="Bytes">What the cleanup frees, as measured now; null when it can only be known by doing it (Flatpak).</param>
/// <param name="Recommended">Safe for everyone: comes ticked.</param>
/// <param name="NeedsPassword">Runs as root through polkit (pkexec).</param>
/// <param name="Command">What will run, shown before anything happens; empty for in-app deletions.</param>
/// <param name="Parts">What is inside, biggest first ("Exibir detalhes"): each program's cache, each file in the trash, each package's old versions.</param>
/// <param name="PartsChoosable">The user can keep some parts (trash, caches); root cleanups run as one command and only show their parts.</param>
public sealed record CleanupItem(CleanupKind Kind, string Title, string Explanation, long? Bytes, bool Recommended, bool NeedsPassword, string Command, string? Unavailable = null,
    IReadOnlyList<CleanupPart>? Parts = null, bool PartsChoosable = false);

/// <summary>One piece of a cleanup item, in plain words.</summary>
/// <param name="Key">The entry name inside the item's folder: what the page sends back to delete only the chosen parts.</param>
/// <param name="Redownload">Comes back only by downloading it again (browsers for automation, AUR builds, AI models…).</param>
public sealed record CleanupPart(string Key, string Name, string Note, long Bytes, bool Redownload = false);

/// <summary>A big file in the home folder that nobody opened or changed for a long time.</summary>
public sealed record StaleFile(string Path, long Bytes, int DaysUnused);

/// <summary>
/// "Liberar espaço": measures what can be removed safely (trash, program caches, downloaded packages of the distribution, old system
/// logs, Flatpak runtimes no app uses) and lists big files unused for a long time. Nothing is deleted here without an explicit call from
/// the page, which only happens after the user ticks items and confirms. Caches rebuild themselves; package caches keep the last two
/// versions; logs keep four weeks.
/// </summary>
public static partial class Cleanup
{
    public const int StaleDays = 50;
    public const long StaleMinBytes = 50L << 20;
    private const int MaxStale = 200;

    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string TrashDir => Path.Combine(Home, ".local/share/Trash");
    public static string CacheDir => Path.Combine(Home, ".cache");

    [GeneratedRegex(@"disk space saved:\s*([0-9.]+)\s*([KMGT]i?B)", RegexOptions.CultureInvariant)]
    private static partial Regex PaccacheSaved();
    [GeneratedRegex(@"take up\s*([0-9.]+)\s*([KMGT])", RegexOptions.CultureInvariant)]
    private static partial Regex JournalUsage();

    public static async Task<IReadOnlyList<CleanupItem>> ScanAsync(string distroId, string idLike, CancellationToken ct = default)
    {
        var trash = await Task.Run(() => TrashParts(ct), ct);
        var cache = await Task.Run(() => CacheParts(CacheDir, ct), ct);
        var items = new List<CleanupItem>
        {
            new(CleanupKind.Trash, L.T("Lixeira"), L.T("Arquivos que você já apagou."), await Task.Run(() => DirectorySize(TrashDir, ct), ct), true, false, "", Parts: trash, PartsChoosable: true),
            new(CleanupKind.UserCache, L.T("Caches de programas"), L.T("Miniaturas, navegador e afins em ~/.cache. Se refazem quando preciso (o primeiro uso fica um pouco mais lento). Feche os programas antes."), cache.Sum(p => p.Bytes), true, false, "", Parts: cache, PartsChoosable: true),
        };
        items.Add(await PackageCacheAsync(distroId, idLike, ct));
        items.Add(await JournalAsync(ct));
        if (BoundedProcess.FindSystem("flatpak") is not null)
            items.Add(new(CleanupKind.FlatpakUnused, L.T("Flatpak sem uso"), L.T("Bibliotecas (runtimes) que nenhum app instalado usa mais. O tamanho só é conhecido ao remover."), null, false, false, "flatpak uninstall --unused -y --noninteractive", Parts: await FlatpakPartsAsync(ct)));
        return items;
    }

    /// <summary>Each entry of the trash: name, where it was and when it was deleted (from the .trashinfo next to it).</summary>
    public static IReadOnlyList<CleanupPart> TrashParts(CancellationToken ct, string? trashDir = null)
    {
        string files = Path.Combine(trashDir ?? TrashDir, "files"), info = Path.Combine(trashDir ?? TrashDir, "info");
        if (!Directory.Exists(files)) return [];
        var parts = new List<CleanupPart>();
        foreach (var entry in new DirectoryInfo(files).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            string note = "";
            try
            {
                string text = File.ReadAllText(Path.Combine(info, entry.Name + ".trashinfo"));
                string? from = text.Split('\n').FirstOrDefault(l => l.StartsWith("Path=", StringComparison.Ordinal))?[5..];
                string? when = text.Split('\n').FirstOrDefault(l => l.StartsWith("DeletionDate=", StringComparison.Ordinal))?[13..];
                if (from is not null) note = L.T("Estava em ") + ShortPath(Path.GetDirectoryName(Uri.UnescapeDataString(from)) ?? "");
                if (DateTime.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)) note += (note.Length > 0 ? " · " : "") + L.T("apagado em ") + date.ToString("d", CultureInfo.CurrentCulture);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            parts.Add(new(entry.Name, entry.Name, note, EntrySize(entry, ct)));
        }
        return parts.OrderByDescending(p => p.Bytes).ToArray();
    }

    /// <summary>Each program's folder (or loose file) in ~/.cache with a plain name and what removing it means; Ilmenyx's own cache is left out.</summary>
    public static IReadOnlyList<CleanupPart> CacheParts(string cacheDir, CancellationToken ct)
    {
        if (!Directory.Exists(cacheDir)) return [];
        var parts = new List<CleanupPart>();
        foreach (var entry in new DirectoryInfo(cacheDir).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Name == "ilmenyx") continue;
            var (name, note, redownload) = KnownCache(entry.Name);
            parts.Add(new(entry.Name, name, note, EntrySize(entry, ct), redownload));
        }
        return parts.OrderByDescending(p => p.Bytes).ToArray();
    }

    /// <summary>Plain name and meaning of well-known cache folders; anything else keeps its folder name.</summary>
    internal static (string Name, string Note, bool Redownload) KnownCache(string entry)
    {
        string e = entry.ToLowerInvariant();
        static string Browser() => L.T("Páginas e imagens guardadas pelo navegador. Logins e favoritos não ficam aqui.");
        if (e == "mozilla") return ("Firefox", Browser(), false);
        if (e is "google-chrome" or "chromium" or "bravesoftware" or "microsoft-edge" or "vivaldi" or "opera")
            return (e switch { "google-chrome" => "Google Chrome", "chromium" => "Chromium", "bravesoftware" => "Brave", "microsoft-edge" => "Microsoft Edge", "vivaldi" => "Vivaldi", _ => "Opera" }, Browser(), false);
        if (e == "thumbnails") return (L.T("Miniaturas"), L.T("Prévias de fotos e vídeos nas pastas. Se refazem quando você abre as pastas."), false);
        if (e.StartsWith("mesa_shader_cache", StringComparison.Ordinal) || e is "nvidia" or "radv_builtin_shaders" or "dxvk" or "vkd3d-proton")
            return (L.T("Shaders da placa de vídeo"), L.T("Jogos podem engasgar um pouco nos primeiros minutos enquanto refazem."), false);
        if (e is "paru" or "yay" or "pikaur" or "trizen")
            return (L.T("Pacotes do AUR") + " (" + entry + ")", L.T($"Fontes e pacotes que o {entry} baixou e compilou. Só fazem falta para reinstalar sem baixar de novo."), true);
        if (e is "pip" or "pypoetry" or "uv") return ("Python (" + entry + ")", L.T("Pacotes Python baixados. São baixados de novo quando um projeto precisar."), true);
        if (e is "yarn" or "pnpm" or "deno" or "go-build" or "bun" or "node-gyp" or "typescript")
            return (entry, L.T("Arquivos de ferramentas de programação. São baixados ou gerados de novo quando precisar."), true);
        if (e.StartsWith("ms-playwright", StringComparison.Ordinal) || e == "selenium")
            return (L.T("Navegadores de automação") + " (" + entry + ")", L.T("Navegadores usados por ferramentas de teste. São baixados de novo no próximo uso."), true);
        if (e == "huggingface") return (L.T("Modelos de IA (Hugging Face)"), L.T("Modelos baixados por programas de IA. Costumam ser grandes para baixar de novo."), true);
        if (e is "winetricks" or "protonfixes" or "lutris" or "bottles" or "heroic")
            return (entry, L.T("Instaladores e correções baixados para jogos e programas do Windows. São baixados de novo quando precisar."), true);
        if (e is "spotify") return ("Spotify", L.T("Músicas guardadas para tocar sem baixar de novo."), true);
        if (e is "discord") return ("Discord", L.T("Imagens e arquivos das conversas já vistos."), false);
        if (e == "flatpak") return ("Flatpak", L.T("Arquivos temporários do Flatpak. Os apps instalados não ficam aqui."), false);
        if (e == "appstream") return (L.T("Catálogo de apps"), L.T("Lista de apps da loja de programas. Se refaz na próxima atualização."), false);
        if (e == "fontconfig") return (L.T("Índice de fontes"), L.T("Se refaz sozinho; os programas abrem um pouco mais devagar na primeira vez."), false);
        if (e.EndsWith(".kcache", StringComparison.Ordinal) || e.StartsWith("plasma", StringComparison.Ordinal) || e.StartsWith("kwin", StringComparison.Ordinal) || e.StartsWith("ksycoca", StringComparison.Ordinal) || e is "kde" or "systemsettings" or "kpackage-knshandler" or "krunner")
            return (entry, L.T("Cache da área de trabalho KDE Plasma. Se refaz no próximo login."), false);
        return (entry, L.T($"Cache do programa \"{entry}\". Se você não sabe o que é, pode desmarcar e manter."), false);
    }

    private static long EntrySize(FileSystemInfo entry, CancellationToken ct)
    {
        try { return entry is DirectoryInfo d ? ((d.Attributes & FileAttributes.ReparsePoint) != 0 ? 0 : DirectorySize(d.FullName, ct)) : entry is FileInfo f ? f.Length : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static string ShortPath(string path) => path == Home ? "~" : path.StartsWith(Home + "/", StringComparison.Ordinal) ? "~/" + path[(Home.Length + 1)..] : path;

    /// <summary>Total size of the files under a folder (symbolic links not followed); 0 when it does not exist.</summary>
    public static long DirectorySize(string root, CancellationToken ct, string? skip = null)
    {
        long total = 0;
        if (!Directory.Exists(root)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        foreach (var info in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (skip is not null && info.FullName.StartsWith(Path.Combine(root, skip) + "/", StringComparison.Ordinal)) continue;
            try { total += info.Length; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    /// <summary>Cleaning command of the distribution's package cache. Arch keeps the last two versions (paccache) so a downgrade stays possible.</summary>
    public static (string Command, string Folder) PackagePlan(string distroId, string idLike, bool paccache)
    {
        string all = (distroId + " " + idLike).ToLowerInvariant();
        if (all.Contains("arch")) return (paccache ? "paccache -rk2" : "pacman -Sc --noconfirm", "/var/cache/pacman/pkg");
        if (all.Contains("fedora") || all.Contains("rhel")) return ("dnf clean packages", "/var/cache/libdnf5");
        if (all.Contains("suse")) return ("zypper clean --all", "/var/cache/zypp/packages");
        return ("apt-get clean", "/var/cache/apt/archives");
    }

    private static async Task<CleanupItem> PackageCacheAsync(string distroId, string idLike, CancellationToken ct)
    {
        bool paccache = BoundedProcess.FindSystem("paccache") is not null;
        var (command, folder) = PackagePlan(distroId, idLike, paccache);
        string title = L.T("Pacotes baixados do sistema");
        string text = command.StartsWith("paccache", StringComparison.Ordinal)
            ? L.T("Instaladores antigos dos programas do sistema. Mantém as 2 últimas versões de cada um, para poder voltar. Pede senha.")
            : L.T("Instaladores dos programas do sistema que já foram instalados. Pede senha.");
        string tool = command.Split(' ')[0];
        if (BoundedProcess.FindSystem(tool) is null) return new(CleanupKind.PackageCache, title, text, null, false, true, command, L.T("Ferramenta não encontrada neste sistema: ") + tool);
        long? bytes = null;
        IReadOnlyList<CleanupPart> parts = [];
        try
        {
            if (paccache && tool == "paccache")
            {
                var (_, output) = await BoundedProcess.RunAsync("paccache", ["-dvk2"], TimeSpan.FromSeconds(60), 4 << 20, ct);
                bytes = ParsePaccache(output);
                parts = PackageParts(PaccacheCandidates(output), HostSpawn.HostPath(folder));
            }
            else if (tool != "pacman") parts = PackageParts(null, HostSpawn.HostPath(folder)); // pacman -Sc keeps what is installed: no honest list
            if (bytes is null)
            {
                var (code, output) = await BoundedProcess.RunAsync("du", ["-sb", folder], TimeSpan.FromSeconds(60), 4096, ct);
                if (code == 0 && long.TryParse(output.Split('\t')[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long b)) bytes = b;
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception) { }
        return new(CleanupKind.PackageCache, title, text, bytes, true, true, command, Parts: parts);
    }

    private static async Task<CleanupItem> JournalAsync(CancellationToken ct)
    {
        string title = L.T("Registros antigos do sistema"), text = L.T("Logs com mais de 4 semanas. Os recentes ficam para diagnóstico. Pede senha.");
        const string command = "journalctl --vacuum-time=4weeks";
        if (BoundedProcess.FindSystem("journalctl") is null) return new(CleanupKind.Journal, title, text, null, false, true, command, L.T("Este sistema não usa o journal do systemd."));
        // What --vacuum-time=4weeks removes: archived journal files (name with '@') whose last write is older than four weeks.
        // The folder listing is readable by everyone; the total from --disk-usage is only the fallback, and it would count what stays.
        var files = new List<(string Name, long Bytes, DateTime Modified)>();
        foreach (string root in new[] { "/var/log/journal", "/run/log/journal" }.Select(HostSpawn.HostPath))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in new DirectoryInfo(root).EnumerateFiles("*.journal", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                    files.Add((f.Name, f.Length, f.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        if (files.Count > 0)
        {
            var (bytes, parts) = JournalPlan(files, DateTime.UtcNow);
            return new(CleanupKind.Journal, title, text, bytes, false, true, command, Parts: parts);
        }
        long? total = null;
        try { var (_, output) = await BoundedProcess.RunAsync("journalctl", ["--disk-usage"], TimeSpan.FromSeconds(20), 4096, ct); total = ParseJournal(output); }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { }
        var note = total is { } t ? [new CleanupPart("", L.T("Registros guardados hoje"), L.T("Não deu para ver as datas: o comando apaga só o que tiver mais de 4 semanas, então pode liberar menos que isso."), t)] : Array.Empty<CleanupPart>();
        return new(CleanupKind.Journal, title, text, null, false, true, command, Parts: note);
    }

    /// <summary>Bytes the 4-week vacuum frees and two plain lines: what goes (before which day) and what stays.</summary>
    internal static (long Bytes, IReadOnlyList<CleanupPart> Parts) JournalPlan(IReadOnlyList<(string Name, long Bytes, DateTime Modified)> files, DateTime nowUtc)
    {
        var limit = nowUtc.AddDays(-28);
        var gone = files.Where(f => f.Name.Contains('@') && f.Modified < limit).ToList();
        var kept = files.Except(gone).ToList();
        string day = limit.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        var parts = new List<CleanupPart>();
        parts.Add(gone.Count > 0
            ? new("gone", L.T($"Registros de antes de {day}"), L.T($"{gone.Count} arquivo(s) de registro antigos. São apagados."), gone.Sum(f => f.Bytes))
            : new("gone", L.T("Nada com mais de 4 semanas"), L.T("Todos os registros são recentes; esta limpeza não apagaria nada agora."), 0));
        if (kept.Count > 0)
        {
            string since = kept.Min(f => f.Modified).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
            parts.Add(new("kept", L.T("Registros recentes (ficam)"), L.T($"Desde {since}. Ficam para diagnóstico de problemas."), kept.Sum(f => f.Bytes)));
        }
        return (gone.Sum(f => f.Bytes), parts);
    }

    /// <summary>File names paccache would remove (dry run with -v), signatures included.</summary>
    internal static IReadOnlyList<string> PaccacheCandidates(string output) =>
        output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("==>", StringComparison.Ordinal) && l.Contains(".pkg.tar", StringComparison.Ordinal) && !l.Contains('/')).ToArray();

    /// <summary>Package name of a downloaded package file: "firefox-1:146.0-1-x86_64.pkg.tar.zst" → firefox; "vim_9.1_amd64.deb" → vim.</summary>
    internal static string PackageName(string file)
    {
        string f = file.EndsWith(".sig", StringComparison.Ordinal) ? file[..^4] : file;
        if (f.EndsWith(".deb", StringComparison.Ordinal)) return f.Split('_')[0];
        int cut = f.Contains(".pkg.tar", StringComparison.Ordinal) ? 3 : f.EndsWith(".rpm", StringComparison.Ordinal) ? 2 : 0;
        var pieces = f.Split('-');
        return cut > 0 && pieces.Length > cut ? string.Join('-', pieces[..^cut]) : f;
    }

    /// <summary>Old installers grouped by program, biggest first. <paramref name="only"/> limits to paccache's candidates; null lists the whole folder.</summary>
    internal static IReadOnlyList<CleanupPart> PackageParts(IReadOnlyList<string>? only, string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var wanted = only is null ? null : new HashSet<string>(only, StringComparer.Ordinal);
        var groups = new Dictionary<string, (int Versions, long Bytes)>(StringComparer.Ordinal);
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
            {
                if (wanted is not null && !wanted.Contains(f.Name)) continue;
                if (!(f.Name.Contains(".pkg.tar", StringComparison.Ordinal) || f.Name.EndsWith(".deb", StringComparison.Ordinal) || f.Name.EndsWith(".rpm", StringComparison.Ordinal))) continue;
                string name = PackageName(f.Name);
                var g = groups.GetValueOrDefault(name);
                groups[name] = (g.Versions + (f.Name.EndsWith(".sig", StringComparison.Ordinal) ? 0 : 1), g.Bytes + f.Length);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return groups.OrderByDescending(g => g.Value.Bytes)
            .Select(g => new CleanupPart(g.Key, g.Key, g.Value.Versions == 1 ? L.T("1 instalador") : L.T($"{g.Value.Versions} instaladores"), g.Value.Bytes)).ToArray();
    }

    /// <summary>
    /// Installed Flatpak runtimes and which apps use each. Flatpak has no dry run for --unused, so this shows what is installed and in use;
    /// the removal itself never touches what an app needs.
    /// </summary>
    private static async Task<IReadOnlyList<CleanupPart>> FlatpakPartsAsync(CancellationToken ct)
    {
        try
        {
            var (c1, runtimes) = await BoundedProcess.RunAsync("flatpak", ["list", "--runtime", "--columns=application,branch,installation,size"], TimeSpan.FromSeconds(30), 1 << 20, ct);
            var (c2, apps) = await BoundedProcess.RunAsync("flatpak", ["list", "--app", "--columns=name,runtime"], TimeSpan.FromSeconds(30), 1 << 20, ct);
            return c1 == 0 && c2 == 0 ? FlatpakParts(runtimes, apps) : [];
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception) { return []; }
    }

    internal static IReadOnlyList<CleanupPart> FlatpakParts(string runtimes, string apps)
    {
        var users = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in apps.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length < 2) continue;
            var r = cols[1].Trim().Split('/');
            if (r.Length < 3) continue;
            string key = r[0] + "/" + r[2];
            if (!users.TryGetValue(key, out var list)) users[key] = list = [];
            list.Add(cols[0].Trim());
        }
        var parts = new List<CleanupPart>();
        foreach (var line in runtimes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length < 4) continue;
            string id = cols[0].Trim(), branch = cols[1].Trim(), where = cols[2].Trim() == "system" ? L.T("sistema") : L.T("só seu usuário");
            string note = users.TryGetValue(id + "/" + branch, out var who)
                ? L.T("Usado por ") + string.Join(", ", who.Take(3)) + (who.Count > 3 ? L.T($" e mais {who.Count - 3}") : "")
                : L.T("Nenhum app usa diretamente; pode ser complemento de outra biblioteca (o Flatpak confere ao remover)");
            parts.Add(new(id + "/" + branch + "/" + cols[2].Trim(), id + " " + branch, note + " · " + where, ParseFlatpakSize(cols[3])));
        }
        return parts.OrderBy(p => p.Note.StartsWith(L.T("Usado por "), StringComparison.Ordinal)).ThenByDescending(p => p.Bytes).ToArray();
    }

    /// <summary>"670.7 MB" (LC_ALL=C), "650.2 kB", "1.7 GB" → bytes (decimal units, as flatpak prints them).</summary>
    internal static long ParseFlatpakSize(string text)
    {
        var bits = text.Trim().Replace(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bits.Length != 2 || !double.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return 0;
        return (long)(v * (bits[1].ToUpperInvariant() switch { "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, "TB" => 1e12, _ => 1 }));
    }

    internal static long? ParsePaccache(string output) =>
        PaccacheSaved().Match(output) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (long)(v * Unit(m.Groups[2].Value)) : output.Contains("no candidate packages found", StringComparison.Ordinal) ? 0 : null;

    internal static long? ParseJournal(string output) =>
        JournalUsage().Match(output) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (long)(v * Unit(m.Groups[2].Value)) : null;

    private static double Unit(string u) => u[0] switch { 'K' => 1024d, 'M' => 1024d * 1024, 'G' => 1024d * 1024 * 1024, 'T' => 1024d * 1024 * 1024 * 1024, _ => 1 };

    /// <summary>
    /// Big files (≥ 50 MB) in the home folder not opened nor changed for <see cref="StaleDays"/> days, biggest first. Hidden folders
    /// (settings, caches, game libraries, Flatpak data) are not scanned. Access time counts: with relatime the kernel still updates it once a day.
    /// </summary>
    public static IReadOnlyList<StaleFile> StaleFiles(string root, DateTime nowUtc, CancellationToken ct)
    {
        var found = new List<StaleFile>();
        var limit = nowUtc.AddDays(-StaleDays);
        var stack = new Stack<(string Dir, int Depth)>(); stack.Push((root, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (entry.Name.StartsWith('.')) continue;
                if (entry is DirectoryInfo d) { if (depth < 8) stack.Push((d.FullName, depth + 1)); continue; }
                if (entry is not FileInfo f) continue;
                try
                {
                    if (f.Length < StaleMinBytes) continue;
                    var last = f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc;
                    if (last > limit) continue;
                    found.Add(new(f.FullName, f.Length, (int)(nowUtc - last).TotalDays));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return found.OrderByDescending(f => f.Bytes).Take(MaxStale).ToArray();
    }

    /// <summary>Deletes the contents of a folder (not the folder), skipping files in use or without permission. Returns bytes freed.</summary>
    public static long EmptyDirectory(string root, CancellationToken ct, string? skip = null, IReadOnlySet<string>? only = null)
    {
        long freed = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (skip is not null && entry.Name == skip) continue;
            if (only is not null && !only.Contains(entry.Name)) continue;
            try
            {
                if (entry is DirectoryInfo d && (d.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    long size = DirectorySize(d.FullName, ct);
                    d.Delete(recursive: true); freed += size;
                }
                else { long size = entry is FileInfo f ? f.Length : 0; entry.Delete(); freed += size; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }

    /// <summary>Deletes chosen stale files; only regular files inside the home folder, never through links. Returns bytes freed.</summary>
    public static long DeleteFiles(IEnumerable<StaleFile> files, string home, CancellationToken ct)
    {
        long freed = 0;
        string prefix = Path.GetFullPath(home).TrimEnd('/') + "/";
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(f.Path);
            if (!full.StartsWith(prefix, StringComparison.Ordinal)) continue;
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                long size = info.Length; info.Delete(); freed += size;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }

    /// <summary>
    /// Runs one cleanup item. User items run in the app; root items through polkit with the fixed command shown before.
    /// <paramref name="only"/>: for the trash and the caches, the entries the user kept ticked (null = all). Only direct entry names count.
    /// </summary>
    public static async Task<(bool Ok, string Message, long Freed)> RunAsync(CleanupItem item, CancellationToken ct, IReadOnlySet<string>? only = null)
    {
        if (only is not null && only.Any(k => k.Length == 0 || k.Contains('/') || k is "." or "..")) return (false, L.T("Seleção inválida."), 0);
        switch (item.Kind)
        {
            case CleanupKind.Trash:
                if (only is not null)
                    return (true, "", await Task.Run(() => EmptyDirectory(Path.Combine(TrashDir, "files"), ct, only: only)
                        + EmptyDirectory(Path.Combine(TrashDir, "info"), ct, only: only.Select(k => k + ".trashinfo").ToHashSet(StringComparer.Ordinal)), ct));
                return (true, "", await Task.Run(() => EmptyDirectory(Path.Combine(TrashDir, "files"), ct) + EmptyDirectory(Path.Combine(TrashDir, "info"), ct) + EmptyDirectory(Path.Combine(TrashDir, "expunged"), ct), ct));
            case CleanupKind.UserCache:
                return (true, "", await Task.Run(() => EmptyDirectory(CacheDir, ct, skip: "ilmenyx", only: only), ct));
            case CleanupKind.FlatpakUnused:
                try
                {
                    var (code, output, errors) = await BoundedProcess.RunWithErrorsAsync("flatpak", ["uninstall", "--unused", "-y", "--noninteractive"], TimeSpan.FromMinutes(10), 1 << 20, ct);
                    return code == 0 ? (true, output.Contains("Nothing unused", StringComparison.OrdinalIgnoreCase) ? L.T("Nada sem uso para remover.") : "", 0) : (false, (errors + output).Trim(), 0);
                }
                catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return (false, e.Message, 0); }
            default:
                // Fixed commands from PackagePlan/JournalAsync only: the item's Command never comes from user text.
                if (item.Command is not ("paccache -rk2" or "pacman -Sc --noconfirm" or "dnf clean packages" or "zypper clean --all" or "apt-get clean" or "journalctl --vacuum-time=4weeks"))
                    return (false, L.T("Comando não permitido."), 0);
                var (ok, message) = await PrivilegedFix.RunAsync(item.Command, ct);
                return (ok, ok ? "" : message, ok ? item.Bytes ?? 0 : 0);
        }
    }
}
