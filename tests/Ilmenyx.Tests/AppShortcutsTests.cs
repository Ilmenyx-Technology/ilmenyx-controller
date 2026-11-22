// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class AppShortcutsTests
{
    [Fact]
    public void Exec_words_follow_the_desktop_entry_quoting_rules()
    {
        Assert.Equal("flatpak", AppShortcuts.Quote("flatpak"));
        Assert.Equal("100%%", AppShortcuts.Quote("100%"));
        Assert.Equal("\"Manual:/mnt/HDD/Jogos/Hollow Knight/hollow_knight.exe\"", AppShortcuts.Quote("Manual:/mnt/HDD/Jogos/Hollow Knight/hollow_knight.exe"));
        // A literal quote becomes \" (quoting rule), then its backslash is escaped again by the string rule: \\"
        Assert.Equal("\"a\\\\\"b\"", AppShortcuts.Quote("a\"b"));
        Assert.Equal("\"C:\\\\\\\\x\"", AppShortcuts.Quote("C:\\x")); // one literal backslash = four in the file
        Assert.Equal("\"\\\\$HOME\"", AppShortcuts.Quote("$HOME"));
    }

    [Fact]
    public void Game_entry_opens_the_game_through_ilmenyx_and_has_a_stable_file_name()
    {
        string text = AppShortcuts.Entry("Hollow Knight\nX", ["flatpak", "run", "io.app", AppShortcuts.PlayArg, "Manual:/j/Hollow Knight.exe"], "/c/art.img", "Abre", "io.app");
        Assert.Contains("\nName=Hollow Knight X\n", text);
        Assert.Contains("\nExec=flatpak run io.app --play \"Manual:/j/Hollow Knight.exe\"\n", text);
        Assert.StartsWith("[Desktop Entry]\nType=Application\n", text);
        Assert.Equal(AppShortcuts.GameFileName("Manual:/j/a.exe"), AppShortcuts.GameFileName("Manual:/j/a.exe"));
        Assert.NotEqual(AppShortcuts.GameFileName("Manual:/j/a.exe"), AppShortcuts.GameFileName("Manual:/j/b.exe"));
        Assert.Matches("^ilmenyx-jogo-[0-9a-f]{12}\\.desktop$", AppShortcuts.GameFileName("Steam:367520"));
    }

    [Fact]
    public void Desktop_folder_comes_from_user_dirs_and_write_replaces_with_execute_bit()
    {
        string home = Directory.CreateTempSubdirectory("ilx-short").FullName;
        try
        {
            Assert.Null(AppShortcuts.DesktopFolder(home));
            Directory.CreateDirectory(Path.Combine(home, ".config")); Directory.CreateDirectory(Path.Combine(home, "Área de trabalho"));
            File.WriteAllText(Path.Combine(home, ".config", "user-dirs.dirs"), "# x\nXDG_DESKTOP_DIR=\"$HOME/Área de trabalho\"\n");
            Assert.Equal(Path.Combine(home, "Área de trabalho"), AppShortcuts.DesktopFolder(home));
            File.WriteAllText(Path.Combine(home, ".config", "user-dirs.dirs"), "XDG_DESKTOP_DIR=\"$HOME/\"\n");
            Assert.Null(AppShortcuts.DesktopFolder(home)); // desktop = home: no desktop

            string folder = Path.Combine(home, "apps"), path = AppShortcuts.Write(folder, "x.desktop", "one");
            AppShortcuts.Write(folder, "x.desktop", "two");
            Assert.Equal("two", File.ReadAllText(path));
            if (OperatingSystem.IsLinux()) Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void Installer_temp_root_is_always_a_folder_of_its_own()
    {
        Assert.Equal(FastExec.TempRoot(), FastExec.TempRootIn(null));
        Assert.Equal(FastExec.TempRoot(), FastExec.TempRootIn("relative"));
        Assert.Equal(FastExec.TempRoot(), FastExec.TempRootIn(FastExec.TempRoot()));
        Assert.Equal("/mnt/HDD/" + FastExec.TempFolderName, FastExec.TempRootIn("/mnt/HDD/"));
        Assert.Equal("/mnt/HDD/" + FastExec.TempFolderName, FastExec.TempRootIn("/mnt/HDD/" + FastExec.TempFolderName));
        Assert.NotNull(FastExec.FreeBytes("/tmp/does/not/exist/yet"));
    }
}
