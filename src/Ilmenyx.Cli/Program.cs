using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;

if (HeavyBenchCommand.Handles(args))
{
    if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("Backend disponível apenas no Linux."); return 3; }
    using var heavyStop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; heavyStop.Cancel(); };
    return await HeavyBenchCommand.RunAsync(args, heavyStop.Token);
}
CliArguments command;
try { command = CliArguments.Parse(args); }
catch (ArgumentException e) { Console.Error.WriteLine(e.Message + "\n" + CliArguments.Help); return 2; }
if (command.Operation == CliOperation.Help) { Console.WriteLine(CliArguments.Help); return 0; }
if (!OperatingSystem.IsLinux()) { Console.Error.WriteLine("Backend disponível apenas no Linux."); return 3; }
if (command.Operation == CliOperation.Doctor)
    return DoctorCommand.Run(new LinuxRuntimeDependencies(), Console.Out,
        LinuxRuntimeDependencies.IsInvariantModeRequested(),
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    if (command.Operation == CliOperation.GameSimPlan)
    {
        var plan = GameSimFullPlanner.Create(command.SimulationPlan!);
        if (command.Output is { } planPath) await SnapshotReport.ExportAsync(plan, planPath, stop.Token);
        Console.WriteLine(command.Json ? SnapshotReport.Json(plan) :
            $"Plano GameSim completo · NÃO EXECUTADO\nEntidades: {plan.Entities} · Workers: {plan.Threads}\n" +
            $"Warmup: {plan.WarmupSeconds} s · Medição proposta: {plan.DurationSeconds} s · Timeout proposto: {plan.TimeoutSeconds} s\n" +
            "Plano não executa carga. Runner experimental disponível por opt-in, ainda NÃO VALIDADO com carga real. Consulte Sem_teste.md (SIM-01).");
        return 0;
    }
    if (!new LinuxRuntimeDependencies().CanUseGlobalization(LinuxRuntimeDependencies.IsInvariantModeRequested()))
    {
        Console.Error.WriteLine("Dependencia ICU indisponivel. Os diagnosticos nao foram iniciados. Requer ICU compativel com o runtime .NET 10; consulte docs/pacote-linux-x64.md. Codigo: 5.");
        return 5;
    }
    if (command.Operation == CliOperation.SmartImport)
    {
        if (!Console.IsInputRedirected) { Console.Error.WriteLine("smart-import exige relatório JSON redirecionado na entrada padrão."); return 2; }
        SmartImportReport report;
        try { report = await SmartctlReportImport.ReadAsync(command.Device!, Console.In, stop.Token); }
        catch (TimeoutException e) { Console.Error.WriteLine(e.Message); return 124; }
        if (command.Output is { } importPath)
        {
            if (command.Html) await SmartImportHtmlReport.ExportAsync(report, importPath, stop.Token);
            else await SnapshotReport.ExportAsync(report, importPath, stop.Token);
        }
        if (command.Html) Console.Write(SmartImportHtmlReport.Render(report, stop.Token));
        else Console.WriteLine(command.Json ? SnapshotReport.Json(report) :
            $"SMART importado · {report.Diagnostic.Device} · {report.Diagnostic.Status}\nRelato passed: {SnapshotReport.Describe(report.Diagnostic.ReportedPassed)}\nAnálise offline de relatório histórico; sem nova consulta ou garantia de saúde atual." + MetricBlock(report.Diagnostic));
        return 0;
    }
    if (command.Operation == CliOperation.Smart)
    {
        var diagnostic = await new SmartctlDiagnostic(new SmartctlQuery()).CollectAsync(command.Device!, stop.Token);
        if (command.Output is { } smartPath) await SnapshotReport.ExportAsync(diagnostic, smartPath, stop.Token);
        Console.WriteLine(command.Json ? SnapshotReport.Json(diagnostic) : $"SMART · {diagnostic.Device} · {diagnostic.Status}\nRelato passed: {SnapshotReport.Describe(diagnostic.ReportedPassed)}\nNão representa garantia de saúde." + MetricBlock(diagnostic));
        return 0;
    }
    if (command.Operation == CliOperation.Sensors)
    {
        var readings = await LinuxPlatform.CreateSensorProvider().CollectAsync(stop.Token);
        if (command.Output is { } sensorPath)
        {
            if (command.Html) await SensorHtmlReport.ExportAsync(readings, sensorPath, stop.Token);
            else await SnapshotReport.ExportAsync(readings, sensorPath, stop.Token);
        }
        if (command.Html) Console.Write(SensorHtmlReport.Render(readings, stop.Token));
        else Console.WriteLine(command.Json ? SnapshotReport.Json(readings) : SensorPresentation.Text(readings));
        return 0;
    }
    var snapshot = await LinuxPlatform.CreateHardwareProvider().CollectAsync(stop.Token);
    if (command.Operation == CliOperation.GameSimBenchmark)
    {
        Console.Error.WriteLine("GameSim completo por opt-in: 30.000 entidades, warmup 2 s e carga de CPU. Sem proteção térmica; Ctrl+C cancela. Execução real NÃO VALIDADA.");
        var result = await new GameSimBenchmarkRunner().RunAsync(command.SimulationBenchmark!, stop.Token);
        var report = new GameSimBenchmarkReport("1.0", snapshot, result);
        if (command.Output is { } fullPath) await SnapshotReport.ExportAsync(report, fullPath);
        Console.WriteLine(command.Json ? SnapshotReport.Json(report) :
            $"GameSim completo experimental · {result.Status}\nFrames: {result.MeasuredFrames} · Tempo: {result.MeasuredSeconds:F3} s\n" +
            $"FPS simulação/score: {result.ExperimentalScore?.ToString("F3") ?? "indisponível"}\nLow1 (recíproco p99): {result.Low1Fps?.ToString("F3") ?? "indisponível"}\nComparação Windows NÃO VALIDADA.");
        return result.Status switch { GameSimBenchmarkStatus.Completed => 0, GameSimBenchmarkStatus.Cancelled => 130,
            GameSimBenchmarkStatus.TimedOut => 124, GameSimBenchmarkStatus.SampleLimitReached => 125, _ => 126 };
    }
    if (command.Operation == CliOperation.GameSimValidation)
    {
        var result = await new GameSimValidationRunner().RunAsync(command.Simulation!, stop.Token);
        var report = new GameSimValidationReport("1.0", snapshot, result);
        if (command.Output is { } simPath)
        {
            if (command.Html) await ValidationHtmlReport.ExportAsync(report, simPath);
            else await SnapshotReport.ExportAsync(report, simPath);
        }
        if (command.Html) Console.Write(ValidationHtmlReport.Render(report));
        else Console.WriteLine(command.Json ? SnapshotReport.Json(report) : $"GameSim reduzido (simulação sintética) · {result.Status}\nPassos: {result.CompletedSteps}/{result.Workload.Steps}\nSHA256 estado: {result.StateSha256 ?? "indisponível"}\nSem FPS ou score; não mede desempenho de um jogo real.");
        return result.Status == GameSimValidationStatus.Cancelled ? 130 : result.Status == GameSimValidationStatus.TimedOut ? 124 : 0;
    }
    if (command.Operation == CliOperation.MemoryValidation)
    {
        var result = await new MemoryValidationRunner().RunAsync(command.Memory!, stop.Token);
        var report = new MemoryValidationReport("1.0", snapshot, result);
        if (command.Output is { } memoryPath)
        {
            if (command.Html) await ValidationHtmlReport.ExportAsync(report, memoryPath);
            else await SnapshotReport.ExportAsync(report, memoryPath);
        }
        if (command.Html) Console.Write(ValidationHtmlReport.Render(report));
        else Console.WriteLine(command.Json ? SnapshotReport.Json(report) : $"Validação de memória reduzida · {result.Status}\nPassos: {result.CompletedTraversalSteps}/{result.RequestedTraversalSteps}\nSoma leitura/cópia: {result.ReadSum?.ToString() ?? "indisponível"}/{result.CopySum?.ToString() ?? "indisponível"}\nSHA256 cópia: {result.CopySha256 ?? "indisponível"}\nNão mede latência, GB/s ou score de RAM.");
        return result.Status == MemoryValidationStatus.Cancelled ? 130 : result.Status == MemoryValidationStatus.TimedOut ? 124 : 0;
    }
    if (command.Html && command.Operation == CliOperation.Info)
    {
        if (command.Output is { } htmlPath) await HardwareHtmlReport.ExportAsync(snapshot, htmlPath, stop.Token);
        Console.Write(HardwareHtmlReport.Render(snapshot, stop.Token));
        return 0;
    }
    if (command.Operation == CliOperation.CpuBenchmark)
    {
        if (command.Benchmark!.Profile == CpuWorkloadProfile.Full)
            Console.Error.WriteLine("Carga completa de CPU iniciada por opt-in. Sem proteção térmica implementada; Ctrl+C para cancelar.");
        // Collection and serialization stay outside the renderer's measured interval.
        var render = await new CpuBenchmarkRunner().RunAsync(command.Benchmark!, stop.Token);
        var result = render.Result;
        var report = new CpuBenchmarkReport("1.0", snapshot, result);
        // Preserve an explicitly cancelled result if the caller requested an output file.
        if (command.Output is { } path)
        {
            if (command.Html) await ValidationHtmlReport.ExportAsync(report, path);
            else await SnapshotReport.ExportAsync(report, path);
        }
        if (command.Html) Console.Write(ValidationHtmlReport.Render(report));
        else Console.WriteLine(command.Json ? SnapshotReport.Json(report) :
            $"Ilmenyx CPU · {result.Workload.Id}\nEstado: {result.Status}\nThreads: {result.Threads}\n" +
            $"Passadas: {result.CompletedPasses}\nAmostras concluídas: {result.CompletedSamples}\n" +
            $"SHA256 BGRA: {result.ImageSha256}\nScore experimental: {result.ExperimentalScore?.ToString("F3") ?? "indisponível"}\n" +
            "Comparação com Windows NÃO VALIDADA; validação reduzida não mede desempenho.");
        return result.Status == CpuRunStatus.Cancelled ? 130 : result.Status == CpuRunStatus.TimedOut ? 124 : 0;
    }
    if (command.Output is { } output) await SnapshotReport.ExportAsync(snapshot, output, stop.Token);
    Console.WriteLine(command.Operation == CliOperation.Capabilities
        ? command.Json ? JsonSerializer.Serialize(snapshot.Capabilities, SnapshotReport.Options)
            : string.Join("\n", snapshot.Capabilities.Select(c => $"{c.Name}: {c.Status} — {c.Reason}"))
        : command.Json ? SnapshotReport.Json(snapshot) : SnapshotReport.Text(snapshot));
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Coleta cancelada ou timeout de 5 segundos."); return 130; }
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or AggregateException)
{ Console.Error.WriteLine("Falha: " + e.Message); return 1; }

static string MetricBlock(SmartDiagnostic d) => SmartMetricText.Lines(d) is { Count: > 0 } lines ? "\nMétricas brutas (sem veredito):\n" + string.Join("\n", lines) : "";
