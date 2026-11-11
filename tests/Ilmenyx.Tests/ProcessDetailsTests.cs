// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class ProcessDetailsTests
{
    [Theory] // real unit names seen on KDE Plasma 6 (CachyOS), GNOME and Flatpak
    [InlineData("app-firefox@cf5e1c019cf74825b18010c18c2ea131.service", "firefox")]
    [InlineData("app-flatpak-io.github.ilmenyx_technology.IlmenyxController-1042234280.scope", "io.github.ilmenyx_technology.IlmenyxController")]
    [InlineData("app-gnome-org.gnome.Nautilus-4567.scope", "org.gnome.Nautilus")]
    [InlineData("app-org.kde.konsole@0a1b2c.service", "org.kde.konsole")]
    [InlineData("app-steam\\x2dnative-123.scope", "steam-native")]
    public void AppIdFollowsSystemdAppUnits(string unit, string id) => Assert.Equal(id, ProcessGroups.AppId(unit));

    [Theory]
    [InlineData(null)]
    [InlineData("plasma-kwin_wayland.service")]
    [InlineData("session-2.scope")]
    [InlineData("app-firefox")]
    public void AppIdIsNullOutsideApps(string? unit) => Assert.Null(ProcessGroups.AppId(unit));

    [Fact]
    public void AppUnitIsTheAppSegmentEvenForChildScopes()
    {
        Assert.Equal("app-firefox@cf5e.service", ProcessDetails.ParseAppUnit("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-firefox@cf5e.service/tab(4182).scope\n"));
        Assert.Null(ProcessDetails.ParseAppUnit("0::/user.slice/user-1000.slice/user@1000.service/session.slice/plasma-kwin_wayland.service\n"));
        Assert.Null(ProcessDetails.ParseAppUnit("0::/\n"));
    }

    [Fact]
    public void UidIoAndUserNamesAreParsed()
    {
        Assert.Equal(1000, ProcessDetails.ParseUid("Name:\tbash\nUid:\t1000\t1000\t1000\t1000\nGid:\t1000\n"));
        Assert.Null(ProcessDetails.ParseUid("Name:\tbash\n"));
        Assert.Equal((4096L, 8192L), ProcessDetails.ParseIo("rchar: 1\nwchar: 2\nread_bytes: 4096\nwrite_bytes: 8192\ncancelled_write_bytes: 0\n"));
        Assert.Null(ProcessDetails.ParseIo("rchar: 1\n"));
        var users = UserNames.Parse("root:x:0:0::/root:/bin/bash\npyfsr:x:1000:1000::/home/pyfsr:/bin/zsh\nbad line\n");
        Assert.Equal("root", users[0]); Assert.Equal("pyfsr", users[1000]); Assert.Equal(2, users.Count);
    }

    [Fact]
    public void HostDumpBlocksSplitByPid()
    {
        var blocks = ProcessDetails.SplitBlocks("@12\nread_bytes: 1\nwrite_bytes: 2\n@34\nread_bytes: 3\nwrite_bytes: 4\n").ToList();
        Assert.Equal([12, 34], blocks.Select(b => b.Pid));
        Assert.Equal((3L, 4L), ProcessDetails.ParseIo(blocks[1].Block));
    }

    [Fact]
    public void DesktopEntryNamePrefersTheUserLanguage()
    {
        string text = "[Desktop Entry]\nName=Files\nName[pt_BR]=Arquivos\nIcon=org.gnome.Nautilus\n[Desktop Action new]\nName=New window\nIcon=other\n";
        Assert.Equal(("Arquivos", "org.gnome.Nautilus"), DesktopApps.Parse(text, "pt_BR"));
        Assert.Equal(("Files", "org.gnome.Nautilus"), DesktopApps.Parse(text, "en_US"));
    }

    [Fact]
    public void DesktopAppsFindsTheFileAndAPngIcon()
    {
        string root = Directory.CreateTempSubdirectory("ilx-apps").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "applications"));
            File.WriteAllText(Path.Combine(root, "applications", "firefox.desktop"), "[Desktop Entry]\nName=Firefox\nIcon=firefox\n");
            Directory.CreateDirectory(Path.Combine(root, "icons/hicolor/48x48/apps"));
            File.WriteAllBytes(Path.Combine(root, "icons/hicolor/48x48/apps/firefox.png"), [0x89, 0x50]);
            var app = new DesktopApps([root]).Find("firefox");
            Assert.Equal("Firefox", app!.Name);
            Assert.EndsWith("48x48/apps/firefox.png", app.IconPng);
            Assert.Null(new DesktopApps([root]).Find("missing"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void GroupsSumAppsAndLeaveServicesOut()
    {
        var list = new[]
        {
            new ProcessUsage(10, "firefox", 5, 100, 1000, 1, 1, "app-firefox@a.service", 10, 0),
            new ProcessUsage(11, "Isolated Web Co", 2, 50, 1000, 10, 2, "app-firefox@a.service", null, null),
            new ProcessUsage(20, "kwin_wayland", 9, 70, 1000, 1, 3, null),
        };
        var groups = ProcessGroups.Build(list);
        var g = Assert.Single(groups);
        Assert.Equal("firefox", g.AppId); Assert.Equal(7, g.CpuPercent); Assert.Equal(150, g.ResidentBytes); Assert.Equal(10, g.ReadBps);
        Assert.True(ProcessGroups.IsSessionCritical("kwin_wayland")); Assert.False(ProcessGroups.IsSessionCritical("firefox"));
    }

    [Fact]
    public async Task SignalRefusesAReusedPidAndTheAppItself()
    {
        string proc = Directory.CreateTempSubdirectory("ilx-proc").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(proc, "4242"));
            File.WriteAllText(Path.Combine(proc, "4242", "stat"), "4242 (x) S 1 1 1 0 -1 0 0 0 0 0 5 5 0 0 20 0 1 0 900 100 3 0 0");
            Assert.NotNull(await ProcessDetails.SignalAsync(4242, 899, false, proc)); // different start: another process now has this pid
            Assert.NotNull(await ProcessDetails.SignalAsync(Environment.ProcessId, 0, false, proc));
            Assert.NotNull(await ProcessDetails.SignalAsync(1, 0, true, proc));
        }
        finally { Directory.Delete(proc, true); }
    }
}
