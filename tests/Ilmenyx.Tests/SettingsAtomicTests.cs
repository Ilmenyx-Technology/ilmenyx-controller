// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Temporary files only; the real ~/.config/ilmenyx/settings.json is never touched.
using System.Diagnostics;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

/// <summary>IlmenyxSettings.Update: several pages (and app instances) write the same file; none may lose what another saved.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class SettingsAtomicTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-settings-" + Guid.NewGuid().ToString("N"));
    private string PathIn => Path.Combine(root, "cfg/ilmenyx/settings.json");
    public SettingsAtomicTests() { Directory.CreateDirectory(root); }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    [Fact]
    public void TwoPagesWithStaleCopiesKeepEachOthersFields()
    {
        string path = PathIn;
        IlmenyxSettings.Update(path, s => s.OverlayCorner = "top-right");
        // Each "page" loads its own copy at startup, as the real pages do.
        var bench = IlmenyxSettings.Load(path); var fps = IlmenyxSettings.Load(path); var shell = IlmenyxSettings.Load(path); var launcher = IlmenyxSettings.Load(path);
        var run = new BenchRunEntry(DateTimeOffset.UnixEpoch, "CPU", "cpu ok", 1.0);
        bench.BenchRuns = IlmenyxSettings.Update(path, s => s.AddBenchRun(run)).BenchRuns;
        fps.FpsSessions = IlmenyxSettings.Update(path, s => s.HowManyFpsKey = "k-123").FpsSessions;
        IlmenyxSettings.Update(path, s => s.ThemeLight = true);
        var profile = new GameProfile { MangoHud = true };
        IlmenyxSettings.Update(path, s => s.GameProfiles["Steam:1"] = profile);
        IlmenyxSettings.Update(path, s => s.TweakBackup.TryAdd("power", "balanced"));
        Assert.Empty(shell.BenchRuns); Assert.Null(launcher.HowManyFpsKey); // the stale copies really are stale

        var disk = IlmenyxSettings.Load(path);
        Assert.Equal("top-right", disk.OverlayCorner);
        Assert.Equal("cpu ok", Assert.Single(disk.BenchRuns).Summary);
        Assert.Equal("k-123", disk.HowManyFpsKey);
        Assert.True(disk.ThemeLight);
        Assert.True(disk.GameProfiles["Steam:1"].MangoHud);
        Assert.Equal("balanced", disk.TweakBackup["power"]);
        Assert.Single(bench.BenchRuns); // the page refreshes its copy from the returned state

        // The legacy whole-copy Save is exactly the bug this replaces: a stale copy erases the others.
        shell.Save(path);
        Assert.Empty(IlmenyxSettings.Load(path).BenchRuns);
    }

    [Fact]
    public void ConcurrentUpdatesFromManyThreadsKeepEveryChange()
    {
        string path = PathIn;
        const int threads = 8, perThread = 40;
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            for (int i = 0; i < perThread; i++) { string exe = $"/g/{t}/{i}"; IlmenyxSettings.Update(path, s => s.CustomGames.Add(new() { Name = exe, Exe = exe })); }
        });
        var disk = IlmenyxSettings.Load(path);
        Assert.Equal(threads * perThread, disk.CustomGames.Select(c => c.Exe).Distinct().Count());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void UpdateAndGetReturnsValueComputedUnderTheLock()
    {
        string path = PathIn;
        Assert.True(IlmenyxSettings.UpdateAndGet(path, s => s.TweakBackup.TryAdd("mouse", "default")));
        Assert.False(IlmenyxSettings.UpdateAndGet(path, s => s.TweakBackup.TryAdd("mouse", "flat"))); // first original wins
        Assert.Equal("default", IlmenyxSettings.Load(path).TweakBackup["mouse"]);
    }

    [Fact]
    public void MissingOrCorruptFileStartsFromDefaultsAndKeepsACopyOfTheCorruptOne()
    {
        string path = PathIn;
        Assert.False(IlmenyxSettings.Update(path, s => { }).ThemeLight); Assert.True(File.Exists(path)); // missing dir + file: defaults
        File.WriteAllText(path, "{corrupt");
        var saved = IlmenyxSettings.Update(path, s => s.DeviceToasts = false);
        Assert.False(saved.DeviceToasts); Assert.False(IlmenyxSettings.Load(path).DeviceToasts);
        Assert.Equal("{corrupt", File.ReadAllText(path + ".corrupt"));
        File.WriteAllText(path, """{"BenchRuns": null, "GameProfiles": null, "CustomGames": null, "TweakBackup": null, "FpsSessions": null}""");
        var fixedUp = IlmenyxSettings.Update(path, s => s.AddBenchRun(new(DateTimeOffset.UnixEpoch, "GPU", "x", null)));
        Assert.Single(fixedUp.BenchRuns); Assert.Empty(fixedUp.GameProfiles);
    }

    [Fact]
    public void UnreadableExistingFileIsNotOverwrittenWithDefaults()
    {
        string path = PathIn;
        IlmenyxSettings.Update(path, s => s.HowManyFpsKey = "keep-me");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (Environment.UserName == "root") return; // root reads anything
            Assert.Throws<IOException>(() => IlmenyxSettings.Update(path, s => s.ThemeLight = true));
        }
        finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        Assert.Equal("keep-me", IlmenyxSettings.Load(path).HowManyFpsKey);
    }

    [Fact]
    public void FileModeStays0600AndLeftoverLockFileDoesNotBlock()
    {
        string path = PathIn;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".lock", "left by a crashed instance"); // a stale lock file is just a file: flock is gone with its process
        var sw = Stopwatch.StartNew();
        IlmenyxSettings.Update(path, s => s.HowManyFpsKey = "secret");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), sw.Elapsed.ToString());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        IlmenyxSettings.Update(path, s => s.ThemeLight = true);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void LockHeldByAnotherProcessIsHonouredThenTimesOutInsteadOfBlockingForever()
    {
        if (!File.Exists("/usr/bin/flock")) return; // util-linux flock(1) plays the "other instance"
        string path = PathIn;
        IlmenyxSettings.Update(path, s => s.OverlayScale = 1.25); // creates dir + lock file
        var old = IlmenyxSettings.LockTimeout;
        using var holder = Process.Start(new ProcessStartInfo("/usr/bin/flock", ["-x", path + ".lock", "-c", "echo held; sleep 30"]) { RedirectStandardOutput = true })!;
        try
        {
            Assert.Equal("held", holder.StandardOutput.ReadLine());
            IlmenyxSettings.LockTimeout = TimeSpan.FromMilliseconds(400);
            var sw = Stopwatch.StartNew();
            IlmenyxSettings.Update(path, s => s.OverlayCorner = "bottom-left");
            sw.Stop();
            Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(350), "should have waited for the other process: " + sw.Elapsed); // same flock as flock(1)
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), "must not block for ever: " + sw.Elapsed);
            var disk = IlmenyxSettings.Load(path);
            Assert.Equal("bottom-left", disk.OverlayCorner); Assert.Equal(1.25, disk.OverlayScale);
        }
        finally { IlmenyxSettings.LockTimeout = old; try { holder.Kill(true); } catch (InvalidOperationException) { } holder.WaitForExit(); }
        // Holder gone (as when an instance crashes): the kernel dropped its flock, so the next update gets it at once.
        var sw2 = Stopwatch.StartNew();
        IlmenyxSettings.Update(path, s => s.ThemeLight = true);
        Assert.True(sw2.Elapsed < TimeSpan.FromMilliseconds(300), sw2.Elapsed.ToString());
    }
}
