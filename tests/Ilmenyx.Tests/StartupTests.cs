// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Diretórios temporários; nada do usuário real é tocado.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class StartupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-st-" + Guid.NewGuid().ToString("N"));
    private string Cfg => Path.Combine(root, "cfg"); private string Data => Path.Combine(root, "data"); private string Sys => Path.Combine(root, "etc/xdg/autostart");
    public StartupTests() { Directory.CreateDirectory(Path.Combine(Cfg, "autostart")); Directory.CreateDirectory(Sys); Directory.CreateDirectory(Data); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static string Desktop(string name, string exec, string extra = "") => $"[Desktop Entry]\nType=Application\nName={name}\nComment={name} comment\nExec={exec}\n{extra}\n[Desktop Action x]\nName=Other\n";
    private static IReadOnlyList<StartupEntry> await_sync(LinuxStartupManager m) => m.ListAsync().GetAwaiter().GetResult();
    private LinuxStartupManager Manager(List<IReadOnlyList<string>>? calls = null, string units = "") => new(Cfg, Data, [Sys], (args, _) => { calls?.Add(args); return Task.FromResult((0, args.Contains("list-unit-files") ? units : "")); });

    [Fact]
    public void DesktopParserAndEditorKeepOtherGroupsAndComments()
    {
        string text = "# comment\n[Desktop Entry]\nName=App\nExec=app %U\n\n[Desktop Action x]\nName=Other\nExec=other\n";
        var d = DesktopEntry.Parse(text); Assert.Equal("App", d["Name"]); Assert.Equal("app %U", d["Exec"]); Assert.DoesNotContain("Other", d.Values);
        string hidden = DesktopEntry.WithKey(text, "Hidden", "true");
        Assert.Contains("Hidden=true", hidden); Assert.Contains("# comment", hidden); Assert.Contains("[Desktop Action x]\nName=Other", hidden);
        Assert.Equal("true", DesktopEntry.Parse(hidden)["Hidden"]);
        Assert.Equal(-1, hidden.IndexOf("Hidden=true", hidden.IndexOf("[Desktop Action x]", StringComparison.Ordinal), StringComparison.Ordinal)); // inserted inside the right group
        Assert.DoesNotContain("Hidden", DesktopEntry.WithKey(hidden, "Hidden", null));
        Assert.Equal("Exec=app", DesktopEntry.CleanExec("Exec=app %U").Replace("Exec=", "Exec=")[..8]);
        Assert.Equal("app --flag", DesktopEntry.CleanExec("app --flag %U")); Assert.Equal("/usr/bin/app", DesktopEntry.ExeOf("\"/usr/bin/app\" --x %f"));
    }

    [Fact]
    public async Task ListsUserAndSystemEntriesAndUserFileOverridesSystemOne()
    {
        File.WriteAllText(Path.Combine(Cfg, "autostart/mine.desktop"), Desktop("Mine", "/bin/sh -c true"));
        File.WriteAllText(Path.Combine(Sys, "sys.desktop"), Desktop("SystemApp", "/definitely/missing/binary"));
        File.WriteAllText(Path.Combine(Sys, "both.desktop"), Desktop("BothSystem", "/bin/true"));
        File.WriteAllText(Path.Combine(Cfg, "autostart/both.desktop"), Desktop("BothUser", "/bin/true", "Hidden=true\n"));
        File.WriteAllText(Path.Combine(Cfg, "autostart/big.desktop"), new string('x', 70000));
        var list = await Manager().ListAsync();
        Assert.Equal(["BothUser", "Mine", "SystemApp"], list.Select(e => e.Name).ToArray());
        var both = list.Single(e => e.Name == "BothUser"); Assert.False(both.Enabled); Assert.Contains("substitui o do sistema", both.Source); Assert.False(both.System);
        var sys = list.Single(e => e.Name == "SystemApp"); Assert.True(sys.System); Assert.True(sys.FileMissing); Assert.Equal("ARQUIVO NÃO EXISTE", sys.State);
        Assert.Equal("ATIVADO", list.Single(e => e.Name == "Mine").State); Assert.Equal("Mine comment", list.Single(e => e.Name == "Mine").Detail);
    }

    [Fact]
    public async Task DisablingASystemEntryCreatesAUserOverrideAndEnablingDropsIt()
    {
        string sysFile = Path.Combine(Sys, "sys.desktop"); File.WriteAllText(sysFile, Desktop("SystemApp", "/bin/true"));
        var m = Manager(); var entry = (await m.ListAsync()).Single();
        await m.SetEnabledAsync(entry, false);
        string over = Path.Combine(Cfg, "autostart/sys.desktop");
        Assert.True(File.Exists(over)); Assert.Contains("Hidden=true", File.ReadAllText(over)); Assert.Contains("X-Ilmenyx-Override=true", File.ReadAllText(over));
        Assert.Contains("Hidden", File.ReadAllText(over)); Assert.DoesNotContain("Hidden", File.ReadAllText(sysFile)); // system file untouched
        var after = (await m.ListAsync()).Single(); Assert.False(after.Enabled);
        await m.SetEnabledAsync(after, true);
        Assert.False(File.Exists(over)); Assert.True((await m.ListAsync()).Single().Enabled);
        await m.SetEnabledAsync((await m.ListAsync()).Single(), false);
        Assert.Throws<InvalidOperationException>(() => m.Remove((await_sync(m)).Single())); // overrides are re-enabled, never removed
    }

    [Fact]
    public async Task UserEntryToggleRoundTripsAndRemoveRestoreUsesBackupAndStore()
    {
        string file = Path.Combine(Cfg, "autostart/mine.desktop"); File.WriteAllText(file, Desktop("Mine", "/bin/true"));
        var m = Manager(); var e = (await m.ListAsync()).Single();
        await m.SetEnabledAsync(e, false); Assert.False((await m.ListAsync()).Single().Enabled); Assert.Contains("X-GNOME-Autostart-enabled=false", File.ReadAllText(file));
        await m.SetEnabledAsync((await m.ListAsync()).Single(), true); var text = File.ReadAllText(file); Assert.DoesNotContain("Hidden", text); Assert.DoesNotContain("X-GNOME", text);
        var backup = m.Remove((await m.ListAsync()).Single());
        Assert.False(File.Exists(file)); Assert.True(File.Exists(backup.Backup)); Assert.Single(m.Removed()); Assert.Empty(await m.ListAsync());
        m.Restore(m.Removed()[0]); Assert.True(File.Exists(file)); Assert.Empty(m.Removed()); Assert.Single(await m.ListAsync());
    }

    [Fact]
    public async Task SystemEntriesCannotBeRemovedAndForeignPathsAreRefused()
    {
        File.WriteAllText(Path.Combine(Sys, "sys.desktop"), Desktop("SystemApp", "/bin/true"));
        var m = Manager(); var e = (await m.ListAsync()).Single();
        Assert.Throws<InvalidOperationException>(() => m.Remove(e));
        Assert.Throws<InvalidOperationException>(() => m.Remove(e with { System = false, FilePath = "/etc/passwd" }));
        Assert.Throws<IOException>(() => m.Restore(new("Desktop", "x", "/nonexistent", Path.Combine(Cfg, "autostart/x.desktop"), null)));
        Assert.Throws<IOException>(() => m.Restore(new("Desktop", "x", Path.Combine(Data, "b"), "/etc/evil.desktop", null)));
    }

    [Fact]
    public async Task ServicesAreListedFromSystemctlAndToggledWithValidatedNames()
    {
        var calls = new List<IReadOnlyList<string>>();
        var m = Manager(calls, "pipewire.service enabled enabled\nbad name.service enabled enabled\nstatic.service static -\nx;rm.service enabled enabled\n");
        var list = await m.ListAsync(); var unit = Assert.Single(list, e => e.Kind == "Service"); Assert.Equal("pipewire.service", unit.Id); Assert.Equal("pipewire", unit.Name);
        await m.SetEnabledAsync(unit, false);
        Assert.Contains(calls, c => c.SequenceEqual(["--user", "disable", "--", "pipewire.service"]));
        Assert.Single(m.Removed()); // remembered so it stays visible and can be re-enabled
        var again = (await Manager(calls, "").ListAsync()).Single(); Assert.False(again.Enabled); Assert.Contains("desativado pelo Ilmenyx", again.Source);
        await Assert.ThrowsAsync<ArgumentException>(() => m.SetEnabledAsync(unit with { Id = "x;rm -rf.service" }, false));
        await m.RestoreAllAsync(); Assert.Contains(calls, c => c.SequenceEqual(["--user", "enable", "--", "pipewire.service"])); Assert.Empty(m.Removed());
        Assert.Equal(["a.service"], LinuxStartupManager.ParseUnits("a.service enabled enabled\nb.service disabled enabled\n"));
    }

    [Fact]
    public async Task CorruptStoreOrMissingDirectoriesDoNotBreakListing()
    {
        Directory.CreateDirectory(Path.Combine(Cfg, "ilmenyx")); File.WriteAllText(Path.Combine(Cfg, "ilmenyx/startup-removed.json"), "{not json");
        var m = new LinuxStartupManager(Path.Combine(root, "nocfg"), Path.Combine(root, "nodata"), [Path.Combine(root, "nosys")], null);
        Assert.Empty(await m.ListAsync()); Assert.Empty(new LinuxStartupManager(Cfg, Data, [Sys], null).Removed());
    }
}
