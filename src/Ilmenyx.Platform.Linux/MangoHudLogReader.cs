// New Ilmenyx additions: see LICENSING.md.
using System.Text;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Finds the newest MangoHud CSV in Ilmenyx's own log folder and reads only its header and tail (logs grow without bound).</summary>
public static class MangoHudLogReader
{
    public const int HeadBytes = 4096, TailBytes = 256 * 1024;
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);

    public static string DefaultFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local", "share");
        return Path.Combine(data, "ilmenyx", "mangohud");
    }

    public static string ConfigPath() => Path.Combine(Path.GetDirectoryName(Ilmenyx.Core.IlmenyxSettings.DefaultPath())!, "mangohud.conf");

    /// <summary>FPS statistics when a log was written within <see cref="MaxAge"/>; otherwise null (no game under MangoHud is running).</summary>
    public static FrameStats? Read(string folder, DateTimeOffset now)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            var newest = new DirectoryInfo(folder).EnumerateFiles("*.csv").Take(512).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null || now - new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero) > MaxAge) return null;
            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[(int)Math.Min(HeadBytes, stream.Length)];
            stream.ReadExactly(head);
            string text;
            if (stream.Length <= HeadBytes + TailBytes) { stream.Position = 0; text = new StreamReader(stream, Encoding.UTF8).ReadToEnd(); }
            else
            {
                stream.Position = stream.Length - TailBytes;
                var tail = new byte[TailBytes]; stream.ReadExactly(tail);
                string tailText = Encoding.UTF8.GetString(tail);
                text = Encoding.UTF8.GetString(head) + "\n" + tailText[(tailText.IndexOf('\n') + 1)..];
            }
            return MangoHudLog.Parse(text, new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public const long SessionMaxBytes = 32L << 20;

    /// <summary>
    /// Whole-session statistics from the MangoHud CSVs in Ilmenyx's log folder written since <paramref name="since"/> (the game launch).
    /// When several logs were written (a launcher window and the game), the one with the most samples wins. Null when none is usable.
    /// Logs above <see cref="SessionMaxBytes"/> are summarized from the header and the last <see cref="SessionMaxBytes"/>.
    /// </summary>
    public static MangoHudSessionStats? ReadSession(string folder, DateTimeOffset since)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            MangoHudSessionStats? best = null;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.csv").Take(512).Where(f => new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero) >= since - TimeSpan.FromSeconds(2))
                         .OrderByDescending(f => f.LastWriteTimeUtc).Take(16))
            {
                try
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    string text;
                    if (stream.Length <= SessionMaxBytes) text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
                    else
                    {
                        var head = new byte[HeadBytes]; stream.ReadExactly(head);
                        stream.Position = stream.Length - SessionMaxBytes; var tail = new byte[SessionMaxBytes]; stream.ReadExactly(tail);
                        string tailText = Encoding.UTF8.GetString(tail);
                        text = Encoding.UTF8.GetString(head) + "\n" + tailText[(tailText.IndexOf('\n') + 1)..];
                    }
                    if (MangoHudSession.Summarize(text) is { } stats && (best is null || stats.Samples > best.Samples)) best = stats;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return best;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes Ilmenyx's own MangoHud config (0600, atomic). The user's MangoHud.conf is never touched.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static void WriteConfig(string path, string content, string logFolder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(logFolder);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
    }
}
