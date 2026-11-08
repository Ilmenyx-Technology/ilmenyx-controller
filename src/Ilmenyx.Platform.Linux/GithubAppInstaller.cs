// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Installs an app from its project's latest GitHub release with the package of this distribution (.deb with apt, .rpm with dnf or
/// zypper, Arch package with pacman; AppImage in ~/Applications when none of them exists). The file is checked against the
/// release's SHA256SUMS.txt before anything runs, and the system package tool is called through pkexec (the system asks the
/// password; Ilmenyx never sees it). Downloads go to a fresh folder in ~/.cache/ilmenyx/downloads and are deleted afterwards.
/// </summary>
public sealed class GithubAppInstaller(ProgramRunner run, HttpMessageHandler? handler = null, Func<string, bool>? hasTool = null, string? home = null)
{
    private const long MaxDownload = 1L << 30;
    private readonly HttpClient http = Client(handler);
    private readonly Func<string, bool> tool = hasTool ?? (n => BoundedProcess.FindSystem(n) is not null);
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var c = handler is null ? new HttpClient() : new HttpClient(handler);
        c.Timeout = TimeSpan.FromMinutes(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Ilmenyx/0.30 (+https://github.com/Bismuth-Technology/ilmenyx)");
        return c;
    }

    public PackageFormat Format => GithubPackages.Choose(tool);

    /// <summary>AppImage installs live here (and count as installed).</summary>
    public string AppImagePath(InstallableApp app) => Path.Combine(home, "Applications", app.PackageName + ".AppImage");

    public bool AppImageInstalled(InstallableApp app) => File.Exists(AppImagePath(app));

    public async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, IProgress<double>? progress, CancellationToken ct)
    {
        if (!GithubPackages.ValidRepo(app.GithubRepo) || !GithubPackages.ValidPackage(app.PackageName)) return (false, "Origem do app inválida.");
        if (GithubPackages.Arch(RuntimeInformation.OSArchitecture) is not { } arch) return (false, "Processador não suportado por este app.");
        var format = Format;
        string? pkexec = BoundedProcess.FindSystem("pkexec");
        if (format != PackageFormat.AppImage && pkexec is null) return (false, "O pedido de senha do sistema (pkexec) não está instalado.");

        // 1. Latest release: tag and the two files needed.
        string tag; string? assetUrl = null, sumsUrl = null; long size = 0;
        using (var response = await http.GetAsync($"https://api.github.com/repos/{app.GithubRepo}/releases/latest", ct))
        {
            if (!response.IsSuccessStatusCode) return (false, $"Não consegui consultar a versão mais recente ({(int)response.StatusCode}).");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!GithubPackages.ValidVersion(tag)) return (false, "Versão publicada inválida.");
            string want = GithubPackages.AssetName(app.PackageName, tag, arch, format);
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (!url.StartsWith($"https://github.com/{app.GithubRepo}/releases/download/", StringComparison.Ordinal)) continue;
                if (name == want) { assetUrl = url; size = a.GetProperty("size").GetInt64(); }
                else if (name == "SHA256SUMS.txt") sumsUrl = url;
            }
            if (assetUrl is null) return (false, $"A versão {tag} não tem {GithubPackages.Label(format)} para este computador.");
            if (sumsUrl is null) return (false, "A versão não publica somas SHA-256; nada foi instalado.");
            if (size <= 0 || size > MaxDownload) return (false, "Tamanho do pacote inesperado; nada foi instalado.");
        }
        string file = Path.GetFileName(new Uri(assetUrl).AbsolutePath);
        string? expected = GithubPackages.Sha256For(await http.GetStringAsync(sumsUrl, ct), file);
        if (expected is null) return (false, "O pacote não está na lista de somas SHA-256; nada foi instalado.");

        // 2. Download with progress, hashing as it goes.
        string dir = Path.Combine(home, ".cache", "ilmenyx", "downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, file);
        try
        {
            using (var response = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(path);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 20]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read; if (total > size) return (false, "O pacote ficou maior que o anunciado; descartado.");
                    await target.WriteAsync(buffer.AsMemory(0, read), ct); sha.AppendData(buffer, 0, read);
                    progress?.Report(0.9 * total / size);
                }
                if (total != size || Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != expected)
                    return (false, "O arquivo baixado não confere com a soma SHA-256 oficial; descartado.");
            }

            // 3. Install with the distribution's tool (or place the AppImage).
            if (format == PackageFormat.AppImage) return PlaceAppImage(app, path, tag);
            string Sys(string n) => BoundedProcess.FindSystem(n) ?? n; // absolute path for pkexec
            string[] args = format switch
            {
                PackageFormat.Deb => [Sys("apt-get"), "install", "-y", path],
                PackageFormat.Rpm when tool("dnf") => [Sys("dnf"), "install", "-y", path],
                PackageFormat.Rpm => [Sys("zypper"), "--non-interactive", "install", "--allow-unsigned-rpm", path],
                _ => [Sys("pacman"), "-U", "--noconfirm", path],
            };
            var (code, output) = await run(pkexec!, args, TimeSpan.FromMinutes(15), ct);
            progress?.Report(1);
            return code switch
            {
                0 => (true, $"Instalado ({tag})."),
                126 => (false, "Instalação cancelada na janela de senha."),
                127 => (false, "Senha não autorizada."),
                _ => (false, $"Falhou (código {code}). {AppCatalog.LastLine(output)}".Trim()),
            };
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    private (bool, string) PlaceAppImage(InstallableApp app, string downloaded, string tag)
    {
        string target = AppImagePath(app);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(downloaded, target, overwrite: true);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string apps = Path.Combine(home, ".local", "share", "applications"); Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "ilmenyx-" + app.PackageName + ".desktop"),
            $"[Desktop Entry]\nType=Application\nName={app.Name}\nComment={app.Description}\nExec=\"{target}\"\nTerminal=false\nCategories=Office;Graphics;\n");
        return (true, $"Instalado ({tag}) em {target}.");
    }
}
