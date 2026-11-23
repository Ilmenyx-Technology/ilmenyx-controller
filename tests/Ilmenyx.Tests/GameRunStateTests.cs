// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Fake /proc and in-memory data only: no process, network or load.
using System.Buffers.Binary;
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class GameRunStateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-runstate-" + Guid.NewGuid().ToString("N"));
    public GameRunStateTests() { Directory.CreateDirectory(root); }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private void Proc(string pid, string? exeLink, params string[] argv)
    {
        string d = Path.Combine(root, "proc", pid); Directory.CreateDirectory(d);
        File.WriteAllBytes(Path.Combine(d, "cmdline"), System.Text.Encoding.UTF8.GetBytes(string.Concat(argv.Select(a => a + "\0"))));
        File.WriteAllBytes(Path.Combine(d, "environ"), []);
        if (exeLink is not null) File.CreateSymbolicLink(Path.Combine(d, "exe"), exeLink);
    }

    [Fact]
    public void ToolsThatOnlyHaveTheGamePathOnTheCommandLineAreNotTheGame()
    {
        var g = new LibGame("Manual", "e", "G", "/g/mygame", "/g/mygame/run.sh");
        Proc("20", "/usr/bin/kate", "kate", "/g/mygame/readme.txt");          // editor
        Proc("21", "/usr/bin/dolphin", "dolphin", "/g/mygame");                // file manager
        Proc("22", "/usr/bin/bash", "bash", "-c", "cd /g/mygame && ls");       // terminal command
        Proc("23", "/usr/bin/vim", "vim", "/g/mygame/run.sh");                 // editor with the executable itself
        Assert.Empty(GameProcesses.Find(Path.Combine(root, "proc"), g, g.ExePath));
    }

    [Fact]
    public void TheGameIsFoundByBinaryArgv0WineExeAndInterpretedScript()
    {
        var g = new LibGame("Manual", "e", "G", "/g/mygame", "/g/mygame/run.sh");
        Proc("30", "/g/mygame/bin/game", "weird-name");                         // process binary under the folder
        Proc("31", "/usr/bin/x", "/g/mygame/bin/other", "--x");                 // argv[0] under the folder
        Proc("32", "/usr/bin/wine64-preloader", "wine64-preloader", "Z:\\g\\mygame\\game.exe"); // Wine: the .exe is in argv
        Proc("33", "/usr/bin/bash", "bash", "/g/mygame/run.sh");                // script run by an interpreter
        Proc("34", "/usr/bin/kate", "kate", "/g/mygame/readme.txt");            // not the game
        Assert.Equal([30, 31, 32, 33], GameProcesses.Find(Path.Combine(root, "proc"), g, g.ExePath).Order().ToArray());
    }

    [Fact]
    public void SteamGameStillMatchesByEnvironment()
    {
        string d = Path.Combine(root, "proc", "40"); Directory.CreateDirectory(d);
        File.WriteAllBytes(Path.Combine(d, "environ"), System.Text.Encoding.UTF8.GetBytes("SteamGameId=730\0"));
        Assert.Equal([40], GameProcesses.Find(Path.Combine(root, "proc"), new("Steam", "730", "CS", "/s/cs"), null).ToArray());
    }

    private static byte[] Png(int w, int h)
    {
        var b = new byte[33]; new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h); return b;
    }

    private static byte[] Riff(string fourcc, byte[] payload)
    {
        var b = new byte[20 + payload.Length]; "RIFFxxxxWEBP"u8.ToArray().CopyTo(b, 0); System.Text.Encoding.ASCII.GetBytes(fourcc).CopyTo(b, 12);
        payload.CopyTo(b, 20); return b;
    }

    [Fact]
    public void ImageSizeIsReadFromThePngJpegAndWebpHeaders()
    {
        Assert.Equal((300, 9000), GameCovers.ImageSize(Png(300, 9000)));
        // JPEG: SOI, an APP0 segment (length 16), then SOF0 with height 600 and width 400.
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x02, 0x58, 0x01, 0x90, 3, 0, 0, 0, 0, 0 };
        Assert.Equal((400, 600), GameCovers.ImageSize(jpg));
        // WebP VP8X: canvas width-1 and height-1 in 24 bits at 24 and 27.
        var x = new byte[10]; x[4] = 0x0F; x[5] = 0x27; /* 9999 -> width 10000 */ x[7] = 0x63; /* height 100 */
        Assert.Equal((10000, 100), GameCovers.ImageSize(Riff("VP8X", x)));
        // WebP lossless: 14 bits width-1, 14 bits height-1 after the 0x2F signature.
        var l = new byte[10]; l[0] = 0x2F; BinaryPrimitives.WriteUInt32LittleEndian(l.AsSpan(1), (uint)(639 | (479 << 14)));
        Assert.Equal((640, 480), GameCovers.ImageSize(Riff("VP8L", l)));
        // WebP lossy: start code 9D 01 2A, then 16-bit width and height.
        var v = new byte[14]; v[3] = 0x9D; v[4] = 0x01; v[5] = 0x2A; v[6] = 0x80; v[7] = 0x02; v[8] = 0xE0; v[9] = 0x01;
        Assert.Equal((640, 480), GameCovers.ImageSize(Riff("VP8 ", v)));
        Assert.Null(GameCovers.ImageSize("not an image at all, just text........"u8));
    }

    [Fact]
    public void ACoverOver8192PixelsOrAnEmptyFileIsRefusedAndTheOldCoverStays()
    {
        string folder = Path.Combine(root, "covers"), ok = Path.Combine(root, "ok.png"), big = Path.Combine(root, "big.png"), empty = Path.Combine(root, "empty.png");
        File.WriteAllBytes(ok, Png(800, 600)); File.WriteAllBytes(big, Png(9000, 100)); File.WriteAllBytes(empty, []);
        string saved = GameCovers.CopyLocal(ok, folder, "k");
        var e = Assert.Throws<CoverException>(() => GameCovers.CopyLocal(big, folder, "k"));
        Assert.Contains("8192", e.Message);
        Assert.Throws<CoverException>(() => GameCovers.CopyLocal(empty, folder, "k"));
        Assert.True(File.Exists(saved)); Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void ToastQueueShowsOneAtATimeInOrderAndFreesTheSlot()
    {
        var q = new ToastQueue<int>(max: 2);
        Assert.True(q.Offer(1)); Assert.False(q.Offer(2)); Assert.False(q.Offer(3)); Assert.False(q.Offer(4)); // the 4th is dropped (max 2 waiting)
        Assert.True(q.TryNext(out int a)); Assert.Equal(2, a);
        Assert.True(q.TryNext(out int b)); Assert.Equal(3, b);
        Assert.False(q.TryNext(out _));           // empty: slot free
        Assert.True(q.Offer(5));
    }

    [Fact]
    public void RunningGamesReportsOnlyRealChanges()
    {
        int changes = 0; void on() => changes++; RunningGames.Changed += on;
        try
        {
            string key = "Manual:" + Guid.NewGuid();
            Assert.False(RunningGames.Contains(key));
            RunningGames.Set(key, true); RunningGames.Set(key, true);
            Assert.True(RunningGames.Contains(key));
            RunningGames.Set(key, false); RunningGames.Set(key, false);
            Assert.False(RunningGames.Contains(key)); Assert.Equal(2, changes);
        }
        finally { RunningGames.Changed -= on; }
    }
}
