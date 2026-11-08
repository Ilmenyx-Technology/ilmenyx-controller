// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Bounded read of small sysfs/procfs attributes. Never trusts FileInfo.Length: sysfs attributes report a size of 4096
/// (or 0) regardless of their content, so the limit is applied to the characters actually read.
/// </summary>
public static class SysfsText
{
    public static string Read(string path, int maxCharacters = 256)
    {
        if (HostSysfs.Hidden(path)) return HostSysfs.Read(path, maxCharacters); // Flatpak: /sys/module, /sys/firmware live on the host only
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false));
            var buffer = new char[maxCharacters + 1];
            int n = reader.ReadBlock(buffer, 0, buffer.Length);
            if (n > maxCharacters) return "";
            return new string(buffer.Take(n).Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }
}
