// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Fixtures sintéticos no formato binário da Steam (o mesmo de um shortcuts.vdf real com o Hytale Launcher, 2026-11-09).
using System.Text;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class SteamShortcutsTests : IDisposable
{
    private readonly string home = Directory.CreateTempSubdirectory("ilmenyx-shortcuts-").FullName;
    public void Dispose() => Directory.Delete(home, true);

    private static void Str(List<byte> b, string key, string value) { b.Add(0x01); b.AddRange(Encoding.UTF8.GetBytes(key + "\0" + value + "\0")); }
    private static void Int(List<byte> b, string key, uint value) { b.Add(0x02); b.AddRange(Encoding.UTF8.GetBytes(key + "\0")); b.AddRange(BitConverter.GetBytes(value)); }

    private static byte[] File(params (uint Id, string Name, bool Hidden)[] entries)
    {
        var b = new List<byte> { 0x00 }; b.AddRange(Encoding.UTF8.GetBytes("shortcuts\0"));
        for (int i = 0; i < entries.Length; i++)
        {
            b.Add(0x00); b.AddRange(Encoding.UTF8.GetBytes(i + "\0"));
            Int(b, "appid", entries[i].Id); Str(b, "AppName", entries[i].Name); Str(b, "Exe", "\"/usr/bin/flatpak\""); Str(b, "StartDir", "/usr/bin/");
            Str(b, "LaunchOptions", "\"run\" \"com.hypixel.HytaleLauncher\""); Int(b, "IsHidden", entries[i].Hidden ? 1u : 0u);
            b.Add(0x00); b.AddRange(Encoding.UTF8.GetBytes("tags\0")); Str(b, "0", "favorite"); b.Add(0x08); // nested map is skipped
            b.Add(0x08);
        }
        b.Add(0x08); b.Add(0x08);
        return [.. b];
    }

    [Fact]
    public void ReadsTheNonSteamGamesOfTheBinaryFile()
    {
        var s = SteamShortcuts.Parse(File((4104183540, "Hytale Launcher", false), (7, "Escondido", true))).ToList();
        Assert.Equal(2, s.Count);
        Assert.Equal("Hytale Launcher", s[0].Name); Assert.Equal("/usr/bin/flatpak", s[0].Exe); Assert.Equal("/usr/bin/", s[0].StartDir);
        Assert.False(s[0].Hidden); Assert.True(s[1].Hidden);
        Assert.Equal("steam://rungameid/17627334081115062272", s[0].LaunchUri); // 64-bit game ID: app ID << 32 | 0x02000000
    }

    [Fact]
    public void DamagedOrForeignFilesNeverThrow()
    {
        var good = File((1, "A", false), (2, "B", false));
        Assert.Single(SteamShortcuts.Parse(good.AsSpan(0, good.Length - 70))); // cut inside the second entry: keeps the first
        Assert.Empty(SteamShortcuts.Parse([]));
        Assert.Empty(SteamShortcuts.Parse(Encoding.UTF8.GetBytes("\"shortcuts\" { }"))); // text VDF is not this format
        Assert.Empty(SteamShortcuts.Parse(File((0, "Sem ID", false), (5, "  ", false)))); // no app ID or no name: left out
    }

    [Fact]
    public void LibraryListsVisibleShortcutsOnceAndOpensThemThroughSteam()
    {
        foreach (var user in new[] { "111", "222" })
        {
            string dir = Path.Combine(home, ".local/share/Steam/userdata", user, "config");
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(Path.Combine(dir, "shortcuts.vdf"), File((4104183540, "Hytale Launcher", false), (9, "Escondido", true)));
        }
        var g = Assert.Single(new LinuxGameLibrary(home).NonSteam());
        Assert.Equal(LinuxGameLibrary.NonSteamStore, g.Store); Assert.False(g.IsSteam); Assert.Equal("Steam (não Steam)", g.Subtitle);
        Assert.Equal("Hytale Launcher", g.Name); Assert.Equal("steam://rungameid/17627334081115062272", g.LaunchUri);
    }
}
