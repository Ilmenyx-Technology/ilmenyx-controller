// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class MangoHudLogReaderTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-mhlog-" + Guid.NewGuid().ToString("N"));
    public MangoHudLogReaderTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mangohud-glxgears.csv"));

    [Fact]
    public void RecentLogIsReadAndStaleOrMissingLogIsNot()
    {
        string file = Path.Combine(dir, "game.csv"); File.WriteAllText(file, Fixture());
        Assert.NotNull(MangoHudLogReader.Read(dir, DateTimeOffset.UtcNow));
        Assert.Null(MangoHudLogReader.Read(dir, DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Null(MangoHudLogReader.Read(Path.Combine(dir, "missing"), DateTimeOffset.UtcNow));
        File.Delete(file);
        Assert.Null(MangoHudLogReader.Read(dir, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void HugeLogIsReadFromHeaderAndTailOnly()
    {
        var sb = new System.Text.StringBuilder(Fixture().Split('\n')[0] + "\n" + Fixture().Split('\n')[1] + "\nfps,frametime,elapsed\n");
        for (int i = 1; i <= 30000; i++) sb.Append(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(i < 29000 ? 20 : 120)},{(i < 29000 ? 50 : 8.3)},{i * 20_000_000L}\n"));
        File.WriteAllText(Path.Combine(dir, "big.csv"), sb.ToString());
        Assert.True(new FileInfo(Path.Combine(dir, "big.csv")).Length > MangoHudLogReader.HeadBytes + MangoHudLogReader.TailBytes);
        var stats = MangoHudLogReader.Read(dir, DateTimeOffset.UtcNow);
        Assert.InRange(stats!.Fps!.Value, 100, 130);
    }

    [Fact]
    public void NewestFileWins()
    {
        string a = Path.Combine(dir, "a.csv"), b = Path.Combine(dir, "b.csv");
        File.WriteAllText(a, "fps,frametime,elapsed\n10,100,100000000\n"); File.WriteAllText(b, "fps,frametime,elapsed\n200,5,100000000\n");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddSeconds(-5)); File.SetLastWriteTimeUtc(b, DateTime.UtcNow);
        Assert.InRange(MangoHudLogReader.Read(dir, DateTimeOffset.UtcNow)!.Fps!.Value, 190, 210);
    }

    [Fact]
    public void ConfigIsWrittenPrivatelyAndAtomically()
    {
        string path = Path.Combine(dir, "cfg", "mangohud.conf"), logs = Path.Combine(dir, "logs");
        MangoHudLogReader.WriteConfig(path, "fps\n", logs);
        Assert.Equal("fps\n", File.ReadAllText(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.True(Directory.Exists(logs));
        MangoHudLogReader.WriteConfig(path, "fps\nframetime\n", logs);
        Assert.Single(Directory.GetFiles(Path.Combine(dir, "cfg")));
    }
}
