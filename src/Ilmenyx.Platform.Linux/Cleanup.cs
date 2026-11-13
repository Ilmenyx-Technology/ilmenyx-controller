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
/// <param name="Recommended">Will not be missed: it rebuilds itself without a download, or nobody touched it for 90+ days. "Só recomendados" ticks these.</param>
/// <param name="IdleDays">Days since anything inside changed; null when unknown.</param>
public sealed record CleanupPart(string Key, string Name, string Note, long Bytes, bool Redownload = false, bool Recommended = false, int? IdleDays = null);

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
    public static string CacheDir => CacheFolder(Home, Environment.GetEnvironmentVariable("XDG_CACHE_HOME"), HostSpawn.InFlatpak);

    /// <summary>Ilmenyx's own caches (art, downloads, update files): never listed nor deleted.</summary>
    private static readonly HashSet<string> OwnCaches = new(StringComparer.Ordinal) { "ilmenyx", "ilmenyx-update" };

    /// <summary>
    /// The user's cache folder: $XDG_CACHE_HOME when set to a real folder, else ~/.cache. Inside the Flatpak the variable points at the
    /// sandbox's own cache, so ~/.cache is used. A value that is the home folder itself or above it is ignored: emptying it would delete personal files.
    /// </summary>
    internal static string CacheFolder(string home, string? xdg, bool inFlatpak)
    {
        string fallback = Path.Combine(home, ".cache");
        if (inFlatpak || string.IsNullOrWhiteSpace(xdg) || !Path.IsPathFullyQualified(xdg)) return fallback;
        string full = Path.GetFullPath(xdg).TrimEnd('/'), h = Path.GetFullPath(home).TrimEnd('/');
        if (full.Length == 0 || full == h || (h + "/").StartsWith(full + "/", StringComparison.Ordinal) || !Directory.Exists(full)) return fallback;
        return full;
    }

    [GeneratedRegex(@"disk space saved:\s*([0-9.]+)\s*([KMGT]i?B)", RegexOptions.CultureInvariant)]
    private static partial Regex PaccacheSaved();
    [GeneratedRegex(@"take up\s*([0-9.]+)\s*([KMGT])", RegexOptions.CultureInvariant)]
    private static partial Regex JournalUsage();

    public static async Task<IReadOnlyList<CleanupItem>> ScanAsync(string distroId, string idLike, CancellationToken ct = default)
    {
        var trash = (await Task.Run(() => TrashParts(ct), ct)).Select(p => p with { Recommended = true }).ToArray();
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

    /// <summary>Days without change after which a cache nobody recognises counts as leftover.</summary>
    public const int IdleDaysLeftover = 90;

    /// <summary>
    /// Each program's folder (or loose file) in ~/.cache with a plain name, what removing it means, how long it sits untouched and
    /// whether removing it is recommended; Ilmenyx's own cache is left out. Folders the table does not know are matched to an installed
    /// app by name (<paramref name="apps"/>).
    /// </summary>
    public static IReadOnlyList<CleanupPart> CacheParts(string cacheDir, CancellationToken ct, DesktopApps? apps = null, DateTime? nowUtc = null)
    {
        if (!Directory.Exists(cacheDir)) return [];
        apps ??= new DesktopApps(); var now = nowUtc ?? DateTime.UtcNow;
        var parts = new List<CleanupPart>();
        foreach (var entry in new DirectoryInfo(cacheDir).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (OwnCaches.Contains(entry.Name)) continue;
            var (bytes, newest) = Measure(entry, ct);
            int? idle = newest is { } n ? Math.Max(0, (int)(now - n).TotalDays) : null;
            var known = KnownCache(entry.Name);
            if (known is { } k) { parts.Add(new(entry.Name, k.Name, k.Note, bytes, k.Redownload, k.Safe || idle >= IdleDaysLeftover, idle)); continue; }
            var app = apps.FindByFolder(entry.Name);
            string note = app is not null ? L.T($"Cache do aplicativo {app.Name}, que está instalado. Ele refaz o que precisar; pode ter de baixar algo de novo.")
                : L.T("Não achei um programa instalado com este nome. Pode ser de uma ferramenta de terminal ou sobra de algo desinstalado.");
            parts.Add(new(entry.Name, app?.Name ?? entry.Name, note, bytes, false, idle >= IdleDaysLeftover, idle));
        }
        return parts.OrderByDescending(p => p.Bytes).ToArray();
    }

    /// <summary>Size and newest change under one cache entry (symbolic links not followed).</summary>
    private static (long Bytes, DateTime? Newest) Measure(FileSystemInfo entry, CancellationToken ct)
    {
        try
        {
            if (entry is FileInfo file) return (file.Length, file.LastWriteTimeUtc);
            if (entry is not DirectoryInfo dir || (dir.Attributes & FileAttributes.ReparsePoint) != 0) return (0, null);
            long total = 0; DateTime newest = dir.LastWriteTimeUtc;
            foreach (var f in dir.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                ct.ThrowIfCancellationRequested();
                try { total += f.Length; if (f.LastWriteTimeUtc > newest) newest = f.LastWriteTimeUtc; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return (total, newest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (0, null); }
    }

    private static readonly string[] Browsers = ["mozilla", "firefox", "librewolf", "zen", "google-chrome", "chromium", "bravesoftware", "microsoft-edge", "vivaldi", "opera", "epiphany", "falkon", "thorium"];
    private static readonly string[] Shaders = ["mesa_shader_cache", "nvidia", "radv_builtin_shaders", "dxvk", "vkd3d-proton", "amd"];
    // Desktop caches of GNOME, KDE Plasma, Cinnamon, XFCE, COSMIC (Pop!_OS) and the toolkits: rebuilt at the next login or use, nothing to download.
    private static readonly string[] Desktop = ["fontconfig", "gstreamer-1.0", "gtk-3.0", "gtk-4.0", "glycin", "ibus", "ibus-table", "babl", "gegl-0.4", "imagemagick", "matplotlib", "kde", "plasma", "kwin", "ksycoca", "krunner",
        "kscreen", "ksplash", "ksvg-elements", "ksmserver", "kinfocenter", "kcmshell", "kpackage", "kdeconnect", "drkonqi", "bookmarksrunner", "systemsettings", "icon-cache", "event-sound-cache", "favicons",
        "gnome-shell", "gnome-desktop-thumbnailer", "gnome-control-center", "gnome-calculator", "gnome-screenshot", "nautilus", "cinnamon", "nemo", "mate", "xfce4", "cosmic", "pop-shell", "pop-launcher", "io.elementary",
        "ubuntu-report", "update-manager-core", "motd.legal-displayed", "dconf", "obexd", "wireplumber", "pipewire", "ddcutil", "fish", "starship", "nvim", "vim"];
    private static readonly string[] KdePrefixes = ["ksycoca", "kscreen", "kcmshell", "kpackage", "ksmserver", "ksplash", "ksvg", "kwin", "plasma"];
    private static readonly string[] Stores = ["appstream", "gnome-software", "pamac", "discover", "packagekit", "mintinstall", "pop-shop", "io.elementary.appcenter", "apt-file", "debtap", "flatpak"];
    private static readonly string[] Aur = ["paru", "yay", "pikaur", "trizen", "aurutils"];
    private static readonly string[] Python = ["pip", "pypoetry", "uv", "pipenv", "pipx", "pdm", "hatch", "conda", "torch", "pre-commit"];
    private static readonly string[] DevTools = ["yarn", "pnpm", "deno", "bun", "go-build", "node-gyp", "typescript", "ccache", "sccache", "electron", "electron-builder", "cypress", "puppeteer", "bazel", "nix", "jetbrains", "gh", "cloud-code", "copilot", "vscode-cpptools"];
    private static readonly string[] WindowsGames = ["winetricks", "protonfixes", "lutris", "bottles", "heroic", "umu", "wine", "legendary"];

    /// <summary>
    /// Plain name and meaning of well-known cache folders; null when the folder is not known. <c>Safe</c>: rebuilt by itself without a
    /// download and without anyone noticing, so it is recommended; shaders and anything that must be downloaded again are not.
    /// </summary>
    internal static (string Name, string Note, bool Redownload, bool Safe)? KnownCache(string entry)
    {
        string e = entry.ToLowerInvariant();
        bool Is(string[] names) => names.Any(n => e == n || e.StartsWith(n + "-", StringComparison.Ordinal) || e.StartsWith(n + "_", StringComparison.Ordinal) || e.StartsWith(n + ".", StringComparison.Ordinal));
        if (Is(Browsers))
            return (e switch { "mozilla" or "firefox" => "Firefox", "bravesoftware" => "Brave", "microsoft-edge" => "Microsoft Edge", "chromium" => "Chromium", "vivaldi" => "Vivaldi", "opera" => "Opera", _ when e.StartsWith("google-chrome", StringComparison.Ordinal) => "Google Chrome", _ => entry },
                L.T("Páginas e imagens guardadas pelo navegador. Logins e favoritos não ficam aqui."), false, true);
        if (e == "thumbnails") return (L.T("Miniaturas"), L.T("Prévias de fotos e vídeos nas pastas. Se refazem quando você abre as pastas."), false, true);
        if (Is(Shaders)) return (L.T("Shaders da placa de vídeo") + " (" + entry + ")", L.T("Jogos podem engasgar um pouco nos primeiros minutos enquanto refazem."), false, false);
        if (Is(Aur)) return (L.T("Pacotes do AUR") + " (" + entry + ")", L.T($"Fontes e pacotes que o {entry} baixou e compilou. Só fazem falta para reinstalar sem baixar de novo."), true, false);
        if (Is(Python)) return ("Python (" + entry + ")", L.T("Pacotes Python baixados. Evitam baixar tudo de novo a cada instalação num projeto."), true, false);
        if (Is(DevTools)) return (entry, L.T("Arquivos de ferramentas de programação. São baixados ou gerados de novo quando precisar."), true, false);
        if (e.StartsWith("ms-playwright", StringComparison.Ordinal) || e == "selenium") return (L.T("Navegadores de automação") + " (" + entry + ")", L.T("Navegadores usados por ferramentas de teste. São baixados de novo no próximo uso."), true, false);
        if (e == "huggingface") return (L.T("Modelos de IA (Hugging Face)"), L.T("Modelos baixados por programas de IA. Costumam ser grandes para baixar de novo."), true, false);
        if (Is(WindowsGames)) return (entry, L.T("Instaladores e correções baixados para jogos e programas do Windows. São baixados de novo quando precisar."), true, false);
        if (e == "spotify") return ("Spotify", L.T("Músicas guardadas para tocar sem baixar de novo."), true, false);
        if (e is "discord" or "vesktop" or "slack" or "teams-for-linux" or "telegram-desktop") return (entry, L.T("Imagens e arquivos das conversas já vistos."), false, true);
        if (e is "tracker3" or "tracker" or "baloo") return (L.T("Índice de busca de arquivos") + " (" + entry + ")", L.T("A busca do sistema refaz o índice, o que ocupa o processador por um tempo."), false, false);
        if (e == "evolution" || e == "thunderbird" || e == "geary") return (entry, L.T("Cópias dos e-mails para ler sem conexão. São baixadas de novo do servidor."), true, false);
        if (Is(Stores)) return (L.T("Catálogo de apps") + " (" + entry + ")", L.T("Lista de apps da loja de programas. Se refaz na próxima atualização."), false, true);
        // KDE numbers its caches after the name ("ksycoca6_pt_…", "kcmshell6", "kscreenlocker_greet"): plain prefixes for these.
        if (Is(Desktop) || e.EndsWith(".kcache", StringComparison.Ordinal) || KdePrefixes.Any(k => e.StartsWith(k, StringComparison.Ordinal))) return (entry, L.T("Cache da área de trabalho e dos programas do sistema. Se refaz sozinho no próximo uso."), false, true);
        return null;
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
    /// <remarks>Fedora: dnf5 (Fedora 41+) keeps packages in /var/cache/libdnf5, dnf 4 (RHEL, Alma, Rocky, older Fedora) in /var/cache/dnf.</remarks>
    public static (string Command, string[] Folders) PackagePlan(string distroId, string idLike, bool paccache)
    {
        string all = (distroId + " " + idLike).ToLowerInvariant();
        if (all.Contains("arch")) return (paccache ? "paccache -rk2" : "pacman -Sc --noconfirm", ["/var/cache/pacman/pkg"]);
        if (all.Contains("fedora") || all.Contains("rhel")) return ("dnf clean packages", ["/var/cache/libdnf5", "/var/cache/dnf"]);
        if (all.Contains("suse")) return ("zypper clean --all", ["/var/cache/zypp/packages"]);
        return ("apt-get clean", ["/var/cache/apt/archives"]);
    }

    /// <summary>
    /// Files under a system folder (name, size, last write). Native: read directly. Flatpak: the sandbox does not see the host's /var, so
    /// the host's `find` lists them (through flatpak-spawn, like the other host tools). Missing or unreadable folders give an empty list.
    /// </summary>
    internal static async Task<IReadOnlyList<(string Name, long Bytes, DateTime Modified)>> HostFilesAsync(string folder, int depth, CancellationToken ct, bool? useFind = null)
    {
        if (!(useFind ?? HostSpawn.InFlatpak))
            return await Task.Run(() =>
            {
                if (!Directory.Exists(folder)) return (IReadOnlyList<(string, long, DateTime)>)[];
                try
                {
                    return new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                        .Select(f => (f.Name, f.Length, f.LastWriteTimeUtc)).ToArray();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
            }, ct);
        try
        {
            // Exit code 1 only means some subfolder was unreadable: what was listed still counts.
            var (_, output) = await BoundedProcess.RunAsync("find", [folder, "-maxdepth", (depth + 1).ToString(CultureInfo.InvariantCulture), "-type", "f", "-printf", "%f\\t%s\\t%T@\\n"], TimeSpan.FromSeconds(30), 8 << 20, ct);
            return ParseFindListing(output);
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception) { return []; }
    }

    /// <summary>Lines of `find -printf '%f\t%s\t%T@\n'` (name, bytes, seconds since 1970) → files; malformed lines are skipped.</summary>
    internal static IReadOnlyList<(string Name, long Bytes, DateTime Modified)> ParseFindListing(string output)
    {
        var files = new List<(string, long, DateTime)>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length != 3 || !long.TryParse(cols[1], NumberStyles.None, CultureInfo.InvariantCulture, out long bytes)
                || !double.TryParse(cols[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) continue;
            files.Add((cols[0], bytes, DateTime.UnixEpoch.AddSeconds(seconds)));
        }
        return files;
    }

    private static async Task<CleanupItem> PackageCacheAsync(string distroId, string idLike, CancellationToken ct)
    {
        bool paccache = BoundedProcess.FindSystem("paccache") is not null;
        var (command, folders) = PackagePlan(distroId, idLike, paccache);
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
            var files = new List<(string Name, long Bytes, DateTime Modified)>();
            foreach (string folder in folders) files.AddRange(await HostFilesAsync(folder, 3, ct));
            if (paccache && tool == "paccache")
            {
                var (_, output) = await BoundedProcess.RunAsync("paccache", ["-dvk2"], TimeSpan.FromSeconds(60), 4 << 20, ct);
                bytes = ParsePaccache(output);
                parts = PackageParts(PaccacheCandidates(output), files);
            }
            else if (tool != "pacman") parts = PackageParts(null, files); // pacman -Sc keeps what is installed: no honest list
            if (bytes is null && tool != "pacman" && files.Count > 0) bytes = files.Sum(f => f.Bytes); // apt/dnf/zypper clean empty the whole folder
            foreach (string folder in folders)
            {
                if (bytes is not null) break;
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
        foreach (string root in new[] { "/var/log/journal", "/run/log/journal" })
            files.AddRange((await HostFilesAsync(root, 2, ct)).Where(f => f.Name.EndsWith(".journal", StringComparison.Ordinal)));
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
    internal static IReadOnlyList<CleanupPart> PackageParts(IReadOnlyList<string>? only, IEnumerable<(string Name, long Bytes, DateTime Modified)> files)
    {
        var wanted = only is null ? null : new HashSet<string>(only, StringComparer.Ordinal);
        var groups = new Dictionary<string, (int Versions, long Bytes)>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            if (wanted is not null && !wanted.Contains(f.Name)) continue;
            if (!(f.Name.Contains(".pkg.tar", StringComparison.Ordinal) || f.Name.EndsWith(".deb", StringComparison.Ordinal) || f.Name.EndsWith(".rpm", StringComparison.Ordinal))) continue;
            string name = PackageName(f.Name);
            var g = groups.GetValueOrDefault(name);
            groups[name] = (g.Versions + (f.Name.EndsWith(".sig", StringComparison.Ordinal) ? 0 : 1), g.Bytes + f.Bytes);
        }
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
                // Only what the scan listed (and the user kept ticked): a folder created after the scan, or Ilmenyx's own, stays.
                only ??= item.Parts?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
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
