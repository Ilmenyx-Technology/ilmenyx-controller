// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public enum InstallKind { AppImage, Flatpak, Folder }

/// <summary>
/// "Atualizar agora": downloads the new release for the way Ilmenyx was installed, checks it (SHA-256 of the release and, when gpgv exists,
/// the GPG signature against the release key built into the app) and installs it. AppImage: the file is replaced in place (the running copy
/// keeps working until restart). Flatpak: `flatpak install --user --reinstall` of the bundle. Folder package: no automatic install.
/// Nothing runs as root and nothing is installed if any check fails.
/// </summary>
public static class SelfUpdate
{
    public const string Download = "https://github.com/Ilmenyx-Technology/ilmenyx-controller/releases/download/";
    public const string AppId = "io.github.ilmenyx_technology.IlmenyxController";
    private const long MaxBytes = 400L << 20;

    /// <summary>How this copy runs: the AppImage runtime sets APPIMAGE; the Flatpak has /.flatpak-info.</summary>
    public static InstallKind Kind => HostSpawn.InFlatpak ? InstallKind.Flatpak : Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } ? InstallKind.AppImage : InstallKind.Folder;

    public static bool CanInstall => Kind != InstallKind.Folder;

    /// <summary>Release file for the install kind ("v0.33.0" → "Ilmenyx-Controller-0.33.0-x86_64.AppImage").</summary>
    public static string FileName(string tag, InstallKind kind) => "Ilmenyx-Controller-" + tag.TrimStart('v', 'V') + (kind == InstallKind.Flatpak ? "-x86_64.flatpak" : "-x86_64.AppImage");

    /// <summary>Where downloads go: inside the home folder, so the host's flatpak and gpgv can read them from the sandbox too.</summary>
    public static string WorkDir => Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? Path.GetTempPath(), ".cache", "ilmenyx-update");

    /// <summary>The hash line of a `sha256sum` file ("hex  name"); null when malformed.</summary>
    internal static string? ParseSha256(string text)
    {
        string first = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(15);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Ilmenyx-Controller/" + ReleaseCheck.Current);
        return http;
    }

    /// <summary>Downloads one release file to <paramref name="path"/>, reporting the fraction done; refuses anything above 400 MB.</summary>
    internal static async Task DownloadAsync(HttpClient http, string url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new IOException(L.T("O download falhou (HTTP ") + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ").");
        long? total = response.Content.Headers.ContentLength;
        if (total > MaxBytes) throw new IOException(L.T("Arquivo maior que o esperado; download recusado."));
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(path);
        var buffer = new byte[81920]; long done = 0; int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            done += n;
            if (done > MaxBytes) throw new IOException(L.T("Arquivo maior que o esperado; download recusado."));
            await target.WriteAsync(buffer.AsMemory(0, n), ct);
            if (total is > 0) progress?.Report((double)done / total.Value);
        }
    }

    /// <summary>
    /// Downloads and checks the update. Returns the verified file and a note about the signature (empty when GPG checked it);
    /// throws with a plain message when anything does not match.
    /// </summary>
    public static async Task<(string File, string Note)> FetchAsync(string tag, IProgress<(string Step, double Fraction)>? progress, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        var kind = Kind;
        if (kind == InstallKind.Folder) throw new InvalidOperationException(L.T("Esta cópia é o pacote em pasta: baixe a versão nova pela página de downloads."));
        string name = FileName(tag, kind), url = Download + tag + "/" + name;
        Directory.CreateDirectory(WorkDir);
        foreach (var old in Directory.EnumerateFiles(WorkDir)) try { File.Delete(old); } catch (IOException) { }
        string file = Path.Combine(WorkDir, name);
        using var http = Client(handler);
        progress?.Report((L.T("Baixando a versão ") + tag + "…", 0));
        await DownloadAsync(http, url, file, new Progress<double>(f => progress?.Report((L.T("Baixando a versão ") + tag + "…", f))), ct);
        progress?.Report((L.T("Conferindo o arquivo…"), 1));
        string expected = ParseSha256(await http.GetStringAsync(url + ".sha256", ct)) ?? throw new IOException(L.T("A soma de verificação da release não pôde ser lida."));
        string actual;
        await using (var stream = File.OpenRead(file)) actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        if (actual != expected) { File.Delete(file); throw new IOException(L.T("O arquivo baixado não confere com a soma da release. Nada foi instalado; tente de novo.")); }
        // GPG: the signature must come from the Ilmenyx release key built into this app (not from whatever the server sends).
        string signature = file + ".asc";
        await DownloadAsync(http, url + ".asc", signature, null, ct);
        string note = await VerifySignatureAsync(file, signature, ct);
        return (file, note);
    }

    /// <summary>Empty when the signature is good; a note when gpgv is missing (the SHA-256 over HTTPS still matched); throws when it is bad.</summary>
    internal static async Task<string> VerifySignatureAsync(string file, string signature, CancellationToken ct)
    {
        if (BoundedProcess.FindSystem("gpgv") is null || BoundedProcess.FindSystem("gpg") is null) return L.T("Assinatura GPG não conferida (gpg não instalado); a soma SHA-256 conferiu.");
        string armored = Path.Combine(WorkDir, "ilmenyx-release-key.asc"), keyring = Path.Combine(WorkDir, "ilmenyx-release.gpg");
        await using (var key = typeof(SelfUpdate).Assembly.GetManifestResourceStream("ilmenyx-release-key.asc") ?? throw new IOException(L.T("A chave de assinatura não está no app.")))
        await using (var copy = File.Create(armored)) await key.CopyToAsync(copy, ct);
        var (dearmor, _) = await BoundedProcess.RunAsync("gpg", ["--batch", "--yes", "--dearmor", "-o", keyring, armored], TimeSpan.FromSeconds(20), 4096, ct);
        if (dearmor != 0) return L.T("Assinatura GPG não conferida (o gpg não leu a chave); a soma SHA-256 conferiu.");
        var (code, _) = await BoundedProcess.RunAsync("gpgv", ["--keyring", keyring, signature, file], TimeSpan.FromSeconds(60), 8192, ct);
        if (code != 0) { File.Delete(file); throw new IOException(L.T("A assinatura GPG do arquivo não é da Ilmenyx Technology. Nada foi instalado.")); }
        return "";
    }

    /// <summary>Installs the verified file. AppImage: atomic replace of the running file; Flatpak: reinstall of the bundle for this user.</summary>
    public static async Task InstallAsync(string file, CancellationToken ct)
    {
        try { File.Delete(file + ".asc"); } catch (IOException) { }
        switch (Kind)
        {
            case InstallKind.AppImage:
            {
                string target = Environment.GetEnvironmentVariable("APPIMAGE")!;
                string staged = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + ".update");
                File.Copy(file, staged, overwrite: true);
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(staged, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                File.Move(staged, target, overwrite: true); // rename on the same folder: the old file stays readable for the running copy
                File.Delete(file);
                break;
            }
            case InstallKind.Flatpak:
            {
                var (code, output, errors) = await BoundedProcess.RunWithErrorsAsync("flatpak", ["install", "--user", "--reinstall", "-y", "--noninteractive", file], TimeSpan.FromMinutes(10), 1 << 20, ct);
                if (code != 0) throw new IOException(L.T("O Flatpak não instalou a versão nova: ") + (errors + " " + output).Trim());
                File.Delete(file);
                break;
            }
            default: throw new InvalidOperationException(L.T("Esta cópia é o pacote em pasta: baixe a versão nova pela página de downloads."));
        }
    }

    /// <summary>Starts the new version; the caller closes this one right after.</summary>
    public static void Restart()
    {
        if (Kind == InstallKind.AppImage && Environment.GetEnvironmentVariable("APPIMAGE") is { } appImage)
            Process.Start(new ProcessStartInfo(appImage) { UseShellExecute = false })?.Dispose();
        else if (Kind == InstallKind.Flatpak && BoundedProcess.FindSystem("flatpak") is { } flatpak)
            Process.Start(HostSpawn.Start(flatpak, ["run", AppId], redirect: false))?.Dispose();
    }
}
