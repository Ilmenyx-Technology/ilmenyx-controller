// Adapted from RESET EXPLODE DiskBench.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ilmenyx.Core.Benchmarks;
using Microsoft.Win32.SafeHandles;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// CrystalDiskMark-style storage test on Linux: sequential 1 MiB read/write and 4 KiB random access (1 and 8 in flight)
/// with O_DIRECT so the page cache is bypassed. Uses only one new 0600 file inside a new 0700 subfolder of the chosen
/// directory, never a device; both are removed in finally. Heavy by design: only ever started by an explicit click.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public static class LinuxDiskBench
{
    private const int Block = 1 << 20, Small = 4096;
    private const int O_RDWR = 2, O_CREAT = 0x40, O_EXCL = 0x80, O_DSYNC = 0x1000;
    private static int ODirect => RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm ? 0x10000 : 0x4000;
    private static int ONoFollow => RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm ? 0x8000 : 0x20000;

    [DllImport("libc", SetLastError = true, EntryPoint = "open")] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "close")] private static extern int Close(int fd);
    [DllImport("libc", SetLastError = true, EntryPoint = "fsync")] private static extern int Fsync(int fd);
    [DllImport("libc", SetLastError = true, EntryPoint = "pread")] private static extern unsafe nint PRead(int fd, void* buf, nuint count, long offset);
    [DllImport("libc", SetLastError = true, EntryPoint = "pwrite")] private static extern unsafe nint PWrite(int fd, void* buf, nuint count, long offset);

    private static readonly string[] MemoryBackedFilesystems = ["tmpfs", "ramfs", "devtmpfs"];

    /// <summary>Filesystem type of the longest mount point containing <paramref name="path"/>, from /proc/mounts text; null if unknown.</summary>
    public static string? FilesystemTypeOf(string path, string mountsText)
    {
        string full = Path.GetFullPath(path);
        string? best = null; int bestLength = -1;
        foreach (string line in mountsText.Split('\n'))
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 3) continue;
            string mount = MountedDrives.Unescape(f[1]);
            bool inside = mount == "/" || full == mount || full.StartsWith(mount + "/", StringComparison.Ordinal);
            if (inside && mount.Length > bestLength) { best = f[2]; bestLength = mount.Length; }
        }
        return best;
    }

    /// <summary>Why a memory-backed folder must be refused (it would measure RAM, not storage); null if acceptable.</summary>
    public static string? MemoryBackedReason(string path, string mountsText) =>
        FilesystemTypeOf(path, mountsText) is { } type && MemoryBackedFilesystems.Contains(type)
            ? $"Esta pasta está em {type} (memória): o teste mediria a RAM, não um disco. Escolha uma pasta em disco real."
            : null;

    public static Task<DiskBenchResult> RunAsync(DiskBenchOptions options, CancellationToken ct, IProgress<(string Phase, double Fraction)>? progress = null)
    {
        string? why;
        try { why = DiskBenchOptions.Validate(options, Directory.Exists(options.Directory) ? new DriveInfo(Path.GetFullPath(options.Directory)).AvailableFreeSpace : 0); }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { why = "Não foi possível consultar o espaço livre da pasta: " + e.Message; }
        if (why is null)
        {
            try { why = MemoryBackedReason(options.Directory, File.ReadAllText("/proc/mounts")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = "Não foi possível identificar o sistema de arquivos da pasta."; }
        }
        if (why is not null) return Task.FromResult(Unavailable(options, why));
        return Task.Run(() => Run(options, ct, progress));
    }

    private static DiskBenchResult Unavailable(DiskBenchOptions o, string reason) =>
        new(BenchRunStatus.Unavailable, o.Directory, o.FileMegabytes, null, null, null, null, null, null, null, reason);

    private static unsafe DiskBenchResult Run(DiskBenchOptions options, CancellationToken external, IProgress<(string, double)>? progress)
    {
        long fileBytes = (long)options.FileMegabytes << 20;
        string folder = Path.Combine(Path.GetFullPath(options.Directory), "ilmenyx-bench-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "bench.tmp");
        using var deadline = new CancellationTokenSource(options.EffectiveTimeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        var ct = stop.Token;
        int fd = -1;
        void* mem = null;
        try
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            fd = Open(path, O_RDWR | O_CREAT | O_EXCL | ONoFollow | ODirect | O_DSYNC, 0x180 /* 0600 */);
            if (fd < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                return Unavailable(options, errno == 22
                    ? "O sistema de arquivos desta pasta não aceita E/S direta (O_DIRECT; ex.: tmpfs). Escolha uma pasta em disco real."
                    : $"Não foi possível criar o arquivo de teste (errno {errno}).");
            }
            mem = NativeMemory.AlignedAlloc(Block, 4096);
            var seq = new Span<byte>(mem, Block);
            new Random(42).NextBytes(seq); // incompressible
            // Sequential write
            var sw = Stopwatch.StartNew();
            for (long off = 0; off < fileBytes; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                if (PWrite(fd, mem, Block, off) != Block) return Failed(options, "Falha de escrita no arquivo de teste (disco cheio ou erro de E/S).");
                progress?.Report(("Escrita sequencial (1 MiB)", 0.25 * off / fileBytes));
            }
            if (Fsync(fd) != 0) return Failed(options, "fsync falhou após a escrita sequencial.");
            double seqWrite = options.FileMegabytes / sw.Elapsed.TotalSeconds;
            // Sequential read
            sw.Restart();
            for (long off = 0; off < fileBytes; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                if (PRead(fd, mem, Block, off) != Block) return Failed(options, "Falha de leitura no arquivo de teste.");
                progress?.Report(("Leitura sequencial (1 MiB)", 0.25 + 0.25 * off / fileBytes));
            }
            double seqRead = options.FileMegabytes / sw.Elapsed.TotalSeconds;
            long slots = fileBytes / Small;
            double rIops = RandomLoop(fd, mem, slots, false, options.RandomSeconds, progress, "Leitura 4 KiB aleatória (1 por vez)", 0.50, ct, out double rLat);
            double wIops = RandomLoop(fd, mem, slots, true, options.RandomSeconds, progress, "Escrita 4 KiB aleatória (1 por vez)", 0.65, ct, out double wLat);
            double q8 = ParallelRead(fd, slots, 8, options.RandomSeconds, progress, ct);
            progress?.Report(("Concluído", 1));
            return new(BenchRunStatus.Completed, options.Directory, options.FileMegabytes, seqRead, seqWrite, rIops, wIops, rLat, wLat, q8, null);
        }
        catch (OperationCanceledException)
        {
            return new(external.IsCancellationRequested ? BenchRunStatus.Cancelled : BenchRunStatus.TimedOut, options.Directory, options.FileMegabytes,
                null, null, null, null, null, null, null, "Interrompido: nenhum valor é publicado.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(options, e.Message);
        }
        finally
        {
            if (mem is not null) NativeMemory.AlignedFree(mem);
            if (fd >= 0) Close(fd);
            try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { Directory.Delete(folder); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static DiskBenchResult Failed(DiskBenchOptions o, string reason) => Unavailable(o, reason);

    private static unsafe double RandomLoop(int fd, void* buf, long slots, bool write, int seconds, IProgress<(string, double)>? progress,
        string phase, double start, CancellationToken ct, out double latencyUs)
    {
        var rng = new Random(write ? 7 : 3);
        long ops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            for (int i = 0; i < 64; i++)
            {
                long off = rng.NextInt64(slots) * Small;
                nint r = write ? PWrite(fd, buf, Small, off) : PRead(fd, buf, Small, off);
                if (r != Small) throw new IOException("Falha de E/S no teste aleatório.");
                ops++;
            }
            ct.ThrowIfCancellationRequested();
            progress?.Report((phase, start + 0.15 * Math.Min(1, sw.Elapsed.TotalSeconds / seconds)));
        }
        double secs = sw.Elapsed.TotalSeconds;
        latencyUs = secs / ops * 1e6;
        return ops / secs;
    }

    private static unsafe double ParallelRead(int fd, long slots, int threads, int seconds, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        long total = 0;
        Exception? error = null;
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            int seed = 100 + t;
            tasks[t] = Task.Factory.StartNew(() =>
            {
                void* mem = NativeMemory.AlignedAlloc(Small, 4096);
                try
                {
                    var rng = new Random(seed);
                    long local = 0;
                    while (sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
                        for (int i = 0; i < 32; i++)
                        {
                            if (PRead(fd, mem, Small, rng.NextInt64(slots) * Small) != Small) throw new IOException("Falha de leitura paralela.");
                            local++;
                        }
                    Interlocked.Add(ref total, local);
                }
                catch (IOException e) { error = e; }
                finally { NativeMemory.AlignedFree(mem); }
            }, TaskCreationOptions.LongRunning);
        }
        while (!Task.WaitAll(tasks, 150))
            progress?.Report(("Leitura 4 KiB aleatória (8 em paralelo)", 0.80 + 0.19 * Math.Min(1, sw.Elapsed.TotalSeconds / seconds)));
        ct.ThrowIfCancellationRequested();
        if (error is not null) throw error;
        return total / sw.Elapsed.TotalSeconds;
    }
}
