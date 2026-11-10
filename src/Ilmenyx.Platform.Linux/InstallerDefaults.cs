// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text;

namespace Ilmenyx.Platform.Linux;

/// <summary>Which installer engine built a setup.exe (decides how a default folder can be given to it).</summary>
public enum InstallerEngine { Unknown, InnoSetup, Nsis }

/// <summary>
/// Makes Windows installers open already pointing at C:\Jogos\&lt;game&gt;, so the user does not have to type the folder. Inno Setup
/// takes /DIR=, NSIS takes /D= (must be the last argument). Other engines (MSI, InstallShield…) get nothing; the fast exec's
/// safety net still finds a game installed elsewhere on C:.
/// </summary>
public static class InstallerDefaults
{
    public const string GamesRoot = @"C:\Jogos";
    private const int ScanBytes = 4 << 20; // the engine's markers sit in the loader, well inside the first 4 MB

    public static InstallerEngine Detect(string exe)
    {
        try
        {
            using var f = File.OpenRead(exe);
            var buffer = new byte[(int)Math.Min(ScanBytes, f.Length)];
            int read = f.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var span = buffer.AsSpan(0, read);
            if (span.IndexOf("rDlPtS"u8) >= 0 || span.IndexOf("Inno Setup Setup Data"u8) >= 0) return InstallerEngine.InnoSetup;
            if (span.IndexOf("NullsoftInst"u8) >= 0) return InstallerEngine.Nsis;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return InstallerEngine.Unknown;
    }

    /// <summary>The game's name from the installer's folder ("Hollow Knight - Silksong [FitGirl Repack]" → "Hollow Knight - Silksong"), or from the file when the folder is generic.</summary>
    public static string? GameName(string installer)
    {
        string? Clean(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var sb = new StringBuilder(); int depth = 0;
            foreach (char ch in raw)
            {
                if (ch is '[' or '(' or '{') { depth++; continue; }
                if (ch is ']' or ')' or '}') { if (depth > 0) depth--; continue; }
                if (depth == 0 && !"<>:\"/\\|?*".Contains(ch) && !char.IsControl(ch)) sb.Append(ch is '_' ? ' ' : ch);
            }
            string name = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.', '-');
            return name.Length is >= 2 and <= 80 ? name : null;
        }
        string folder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(installer)) ?? "");
        string[] generic = ["downloads", "transferências", "desktop", "área de trabalho", "documentos", "documents", "tmp", "temp", "instalador", "installer", "setup"];
        if (!generic.Contains(folder.ToLowerInvariant()) && Clean(folder) is { } fromFolder) return fromFolder;
        string file = Path.GetFileNameWithoutExtension(installer);
        return file.ToLowerInvariant() is "setup" or "install" or "installer" or "autorun" ? null : Clean(file.Replace("setup", "", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The command-line argument that pre-fills the install folder, or null when the engine has none or no name is known.</summary>
    public static string? DirArguments(string installer)
    {
        if (GameName(installer) is not { } name) return null;
        string dir = GamesRoot + "\\" + name;
        return Detect(installer) switch
        {
            InstallerEngine.InnoSetup => "/DIR=" + dir,
            InstallerEngine.Nsis => "/D=" + dir,
            _ => null,
        };
    }
}
