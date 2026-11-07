using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core.Benchmarks;

public sealed class CpuRenderOutput
{
    private readonly int[] pixels;
    public CpuRenderOutput(CpuBenchmarkResult result, int[] pixels) { Result = result; this.pixels = pixels; }
    public CpuBenchmarkResult Result { get; }
    public int[] CopyPixels() => (int[])pixels.Clone();
}

public interface ICpuBenchmarkRunner
{
    Task<CpuRenderOutput> RunAsync(CpuBenchmarkOptions options, CancellationToken ct = default,
        IProgress<CpuBenchmarkProgress>? progress = null);
}

/// <summary>Every invocation owns its workers and frame; no shared mutable render state.</summary>
public sealed class CpuBenchmarkRunner : ICpuBenchmarkRunner
{
    public const string AlgorithmVersion = "silex-cpu-pathtracer/1";
    public const string MeasurementVersion = "silex-cpu-runner/1";
    public const string ReferenceCommit = "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6";
    private readonly record struct Tile(int X, int Y, int Width, int Height);

    public static CpuWorkload DescribeWorkload(CpuWorkloadProfile profile) => profile switch
    {
        CpuWorkloadProfile.Validation => new("validation-32x18-spp8", 32, 18, 8, 8, 6),
        CpuWorkloadProfile.Full => new("reset-cpu-v0.16-960x540-spp1024", 960, 540, 1024, 32, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    public Task<CpuRenderOutput> RunAsync(CpuBenchmarkOptions options, CancellationToken ct = default,
        IProgress<CpuBenchmarkProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        // Do not pass ct to Task.Run: even pre-cancellation yields an explicit cancelled result, never a score.
        return Task.Run(() => Run(options, ct, progress));
    }

    public static void Validate(CpuBenchmarkOptions options)
    {
        var workload = DescribeWorkload(options.Profile);
        if (options.Threads is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(options.Threads), "Use de 1 a 256 threads.");
        if (options.Timeout <= TimeSpan.Zero || options.Timeout > TimeSpan.FromMinutes(20))
            throw new ArgumentOutOfRangeException(nameof(options.Timeout), "Timeout deve ser maior que zero e no máximo 20 minutos.");
        if (options.MinimumDuration < TimeSpan.Zero || options.MinimumDuration > TimeSpan.FromMinutes(10) || options.MinimumDuration >= options.Timeout)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumDuration));
        if (options.Profile == CpuWorkloadProfile.Full && !options.AllowCpuLoad)
            throw new ArgumentException("Carga completa exige consentimento explícito: --allow-cpu-load.");
        if (options.Profile == CpuWorkloadProfile.Validation && (options.MinimumDuration != TimeSpan.Zero || options.Timeout > TimeSpan.FromSeconds(10) || options.Threads > 4))
            throw new ArgumentException("Validação é uma passada reduzida, de 1 a 4 threads, timeout máximo de 10 segundos.");
        _ = checked(workload.Width * workload.Height * workload.SamplesPerPixel);
    }

    private static CpuRenderOutput Run(CpuBenchmarkOptions options, CancellationToken external,
        IProgress<CpuBenchmarkProgress>? progress)
    {
        var workload = DescribeWorkload(options.Profile);
        var tiles = CreateTiles(workload);
        var scene = new CpuPathScene(workload.Width, workload.Height);
        var pixels = new int[workload.Width * workload.Height];
        using var deadline = new CancellationTokenSource(options.Timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(external, deadline.Token);
        long samples = 0;
        int passes = 0;
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        do
        {
            Array.Fill(pixels, unchecked((int)0xFF000000));
            if (stop.IsCancellationRequested) break;
            int nextTile = -1;
            int completedTiles = 0;
            var errors = new ConcurrentQueue<Exception>();
            var startedWorkers = new List<Thread>(options.Threads);
            try
            {
                for (int worker = 0; worker < options.Threads; worker++)
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            int index;
                            while (!stop.IsCancellationRequested && (index = Interlocked.Increment(ref nextTile)) < tiles.Length)
                            {
                                var tile = tiles[index];
                                for (int y = tile.Y; y < tile.Y + tile.Height; y++)
                                {
                                    stop.Token.ThrowIfCancellationRequested();
                                    for (int x = tile.X; x < tile.X + tile.Width; x++)
                                    {
                                        pixels[y * workload.Width + x] = scene.RenderPixel(x, y, workload.Width,
                                            workload.Height, workload.SamplesPerPixel, stop.Token);
                                        Interlocked.Add(ref samples, workload.SamplesPerPixel);
                                    }
                                }
                                int completed = Interlocked.Increment(ref completedTiles);
                                progress?.Report(new CpuBenchmarkProgress(Interlocked.Read(ref samples), completed, tiles.Length, passes));
                            }
                        }
                        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                        catch (Exception e) { errors.Enqueue(e); stop.Cancel(); }
                    })
                    { IsBackground = true, Name = $"Ilmenyx CPU {worker}" };
                    thread.Start();
                    startedWorkers.Add(thread);
                }
            }
            catch { stop.Cancel(); throw; }
            finally { foreach (var thread in startedWorkers) thread.Join(); }
            if (!errors.IsEmpty) throw new AggregateException("Falha em worker do renderizador.", errors);
            // The deadline timer runs on the thread pool and can fire late under load: check the clock as well.
            if (stop.IsCancellationRequested || timer.Elapsed >= options.Timeout) break;
            passes++;
        } while (timer.Elapsed < options.MinimumDuration);
        timer.Stop();

        var status = external.IsCancellationRequested ? CpuRunStatus.Cancelled
            : deadline.IsCancellationRequested || timer.Elapsed >= options.Timeout ? CpuRunStatus.TimedOut : CpuRunStatus.Completed;
        var imageBytes = new byte[pixels.Length * sizeof(int)];
        for (int i = 0; i < pixels.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(imageBytes.AsSpan(i * 4), pixels[i]);
        string hash = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        double? score = status == CpuRunStatus.Completed && passes > 0 && options.Profile == CpuWorkloadProfile.Full
            ? CpuScore.Points(samples, timer.Elapsed.TotalSeconds) : null;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var result = new CpuBenchmarkResult(AlgorithmVersion, MeasurementVersion, ReferenceCommit, workload, status,
            started, timer.Elapsed.TotalSeconds, options.Threads, options.MinimumDuration.TotalSeconds, options.Timeout.TotalSeconds, passes, samples, score, "unvalidated", hash,
            new CpuBenchmarkEnvironment(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, configuration, 0),
            new[] { "Não é score oficial RESET BENCH; comparação entre Linux e Windows NÃO VALIDADA.",
                "Validação reduzida, cancelamento ou timeout não produzem score.",
                "Sem sensores térmicos ou proteção por temperatura; carga completa somente com opt-in explícito.",
                "Inicialização/JIT, scheduler, afinidade, energia e throttling não são controlados.",
                "Amostras contam apenas pixels concluídos; hash de execução interrompida representa imagem parcial." });
        return new CpuRenderOutput(result, pixels);
    }

    private static Tile[] CreateTiles(CpuWorkload workload)
    {
        var tiles = new List<Tile>();
        for (int y = 0; y < workload.Height; y += workload.TileSize)
            for (int x = 0; x < workload.Width; x += workload.TileSize)
                tiles.Add(new Tile(x, y, Math.Min(workload.TileSize, workload.Width - x), Math.Min(workload.TileSize, workload.Height - y)));
        return tiles.OrderBy(tile => Math.Pow(tile.X + tile.Width / 2.0 - workload.Width / 2.0, 2) +
            Math.Pow((tile.Y + tile.Height / 2.0 - workload.Height / 2.0) * 1.6, 2)).ToArray();
    }
}
