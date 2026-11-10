// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Platform.Linux;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Core;
namespace Ilmenyx.Cli;

public enum CliOperation { Help, Info, Capabilities, CpuBenchmark, Sensors, Smart, MemoryValidation, GameSimValidation, GameSimPlan, GameSimBenchmark, Doctor, SmartImport }
public sealed record CliArguments(CliOperation Operation, bool Json, string? Output, CpuBenchmarkOptions? Benchmark, string? Device = null, bool Html = false, MemoryValidationOptions? Memory = null, GameSimValidationOptions? Simulation = null, GameSimFullPlanOptions? SimulationPlan = null, GameSimBenchmarkOptions? SimulationBenchmark = null)
{
    /// <summary>`--help` text in the current language; the command syntax stays as typed, only its placeholders (ARQUIVO, NOME…) are translated.</summary>
    public static string Help => string.Join("\n",
        "Ilmenyx · by Ilmenyx Technology",
        L.T("Adaptação independente e não oficial de RESET EXPLODE, de Gustavo (GustavoReset), MIT."),
        L.T("Sem afiliação, patrocínio ou endosso da equipe RESET."),
        CliText.Syntax("""
              doctor
              info [--json|--html] [--output ARQUIVO]
              capabilities [--json]
              sensors [--json|--html] [--output ARQUIVO]
              smart-import --device NOME [--json|--html] [--output ARQUIVO] < RELATORIO_SMARTCTL.json
              smart --device NOME --allow-device-query [--json] [--output ARQUIVO]
              bench sim-full --allow-cpu-load [--threads N] [--duration-seconds 1..10] [--timeout-seconds N] [--json] [--output ARQUIVO]
              bench sim-plan [--threads N] [--duration-seconds 1..10] [--timeout-seconds N] [--json] [--output ARQUIVO]
              bench sim [--threads 1|2|4] [--json|--html] [--output ARQUIVO]
              bench memory [--threads 1|2|4] [--json|--html] [--output ARQUIVO]
              bench cpu [--profile validation|full] [--threads N] [--json|--html] [--output ARQUIVO]
                        [--min-seconds N] [--timeout-seconds N] [--allow-cpu-load]
              bench memory-full --allow-memory-load [--timeout-seconds 5..300] [--json]
              bench disk --directory PASTA --allow-disk-load [--size-mb 64..8192] [--phase-seconds 1..10] [--timeout-seconds N] [--json]
              --help
            """),
        L.T("smart-import: entrada padrao limitada, analise offline; nao consulta disco nem confirma origem/data original."),
        L.T("doctor: requisitos locais, texto somente, sem inventario ou benchmarks."),
        L.T("CPU padrão: validação 32x18, 8 spp, uma passada, sem score."),
        L.T("sim-plan: prepara configuração revisável; NÃO executa carga nem mede FPS/score."),
        L.T("GameSim: 256 entidades, 16 passos fixos; sem FPS ou score."),
        L.T("Memória: validação de algoritmos com buffers pequenos; sem latência, GB/s ou score."),
        L.T("memory-full: aloca ~2,3 GiB (recusa sem 1 GiB livre). disk: um arquivo temporário novo (0600), E/S direta, apagado ao final; nunca dispositivos."),
        L.T("Perfil full exige --allow-cpu-load: carga alta de CPU, sem proteção térmica implementada."),
        L.T("Relatórios locais: revise rótulos e configuração antes de compartilhar; sem anonimização garantida."),
        L.T("Scores experimentais NÃO são comparáveis ao Windows. Exportação exige arquivo novo."));

    public static CliArguments Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"]) return new(CliOperation.Help, false, null, null);
        if (args[0] == "doctor")
        {
            if (args.Length != 1) throw new ArgumentException(L.T("doctor aceita apenas saida textual, sem opcoes, exportacao ou carga."));
            return new(CliOperation.Doctor, false, null, null);
        }
        int offset = 1;
        var operation = args[0] switch
        {
            "sensors" => CliOperation.Sensors,
            "smart" => CliOperation.Smart,
            "smart-import" => CliOperation.SmartImport,
            "info" => CliOperation.Info,
            "capabilities" => CliOperation.Capabilities,
            "bench" when args.Length > 1 && args[1] == "sim-full" => CliOperation.GameSimBenchmark,
            "bench" when args.Length > 1 && args[1] == "sim-plan" => CliOperation.GameSimPlan,
            "bench" when args.Length > 1 && args[1] == "sim" => CliOperation.GameSimValidation,
            "bench" when args.Length > 1 && args[1] == "memory" => CliOperation.MemoryValidation,
            "bench" when args.Length > 1 && args[1] == "cpu" => CliOperation.CpuBenchmark,
            _ => throw new ArgumentException(L.T("Comando desconhecido."))
        };
        if (operation is CliOperation.CpuBenchmark or CliOperation.MemoryValidation or CliOperation.GameSimValidation or CliOperation.GameSimPlan or CliOperation.GameSimBenchmark) offset = 2;
        bool json = false, html = false, allow = false;
        string? output = null, device = null;
        bool deviceQuery = false;
        var profile = CpuWorkloadProfile.Validation;
        int threads = 1, seconds = 0, simDuration = 3;
        int? timeout = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = offset; i < args.Length; i++)
        {
            string flag = args[i];
            if (!seen.Add(flag)) throw new ArgumentException(L.T("Argumento duplicado: ") + flag);
            if (operation is (CliOperation.Smart or CliOperation.SmartImport) && flag == "--device") { device = Value(args, ref i); continue; }
            if (operation == CliOperation.Smart && flag == "--allow-device-query") { deviceQuery = true; continue; }
            if (flag == "--html" && operation is (CliOperation.Info or CliOperation.Sensors or CliOperation.SmartImport or CliOperation.CpuBenchmark or CliOperation.MemoryValidation or CliOperation.GameSimValidation)) { html = true; continue; }
            if (flag == "--json") { json = true; continue; }
            if (flag == "--allow-cpu-load" && operation is (CliOperation.CpuBenchmark or CliOperation.GameSimBenchmark)) { allow = true; continue; }
            if (flag == "--output" && operation != CliOperation.Capabilities) { output = Value(args, ref i); continue; }
            if (flag == "--threads" && operation is (CliOperation.MemoryValidation or CliOperation.GameSimValidation or CliOperation.GameSimPlan or CliOperation.GameSimBenchmark)) { threads = Number(args, ref i); continue; }
            if (operation is (CliOperation.GameSimPlan or CliOperation.GameSimBenchmark) && flag == "--duration-seconds") { simDuration = Number(args, ref i); continue; }
            if (operation is (CliOperation.GameSimPlan or CliOperation.GameSimBenchmark) && flag == "--timeout-seconds") { timeout = Number(args, ref i); continue; }
            if (operation != CliOperation.CpuBenchmark) throw new ArgumentException(L.T("Argumento inválido: ") + flag);
            switch (flag)
            {
                case "--profile":
                    profile = Value(args, ref i) switch
                    {
                        "validation" => CpuWorkloadProfile.Validation,
                        "full" => CpuWorkloadProfile.Full,
                        _ => throw new ArgumentException(L.T("Perfil desconhecido."))
                    }; break;
                case "--threads": threads = Number(args, ref i); break;
                case "--min-seconds": seconds = Number(args, ref i); break;
                case "--timeout-seconds": timeout = Number(args, ref i); break;
                default: throw new ArgumentException(L.T("Argumento inválido: ") + flag);
            }
        }
        if (html && json) throw new ArgumentException(L.T("Escolha --html ou --json, não ambos."));
        CpuBenchmarkOptions? options = null;
        if (operation == CliOperation.CpuBenchmark)
        {
            if (allow && profile != CpuWorkloadProfile.Full) throw new ArgumentException("--allow-cpu-load aplica-se apenas ao perfil full.");
            options = new CpuBenchmarkOptions
            {
                Profile = profile,
                Threads = threads,
                AllowCpuLoad = allow,
                MinimumDuration = TimeSpan.FromSeconds(seconds),
                Timeout = TimeSpan.FromSeconds(timeout ?? (profile == CpuWorkloadProfile.Full ? 120 : 10))
            };
            CpuBenchmarkRunner.Validate(options);
            if (html && profile != CpuWorkloadProfile.Validation) throw new ArgumentException(L.T("HTML CPU é somente validação reduzida; full não suportado."));
        }
        if (operation == CliOperation.Smart && (!deviceQuery || device is null || !SmartctlQuery.ValidDeviceName(device)))
            throw new ArgumentException(L.T("SMART exige --device sda|nvme0n1 e --allow-device-query. Consulta somente leitura pode exigir permissões já existentes."));
        if (operation == CliOperation.SmartImport && (device is null || !SmartctlQuery.ValidDeviceName(device)))
            throw new ArgumentException(L.T("smart-import exige --device sda|nvme0n1 para conferir metadados da entrada; não executa consulta."));
        MemoryValidationOptions? memory = null;
        if (operation == CliOperation.MemoryValidation)
        {
            memory = new() { Threads = threads };
            MemoryValidationRunner.Validate(memory);
        }
        GameSimValidationOptions? simulation = null;
        if (operation == CliOperation.GameSimValidation)
        {
            simulation = new() { Threads = threads };
            GameSimValidationRunner.Validate(simulation);
        }
        GameSimFullPlanOptions? plan = null;
        if (operation == CliOperation.GameSimPlan)
        {
            plan = new() { Threads = threads, Duration = TimeSpan.FromSeconds(simDuration), Timeout = TimeSpan.FromSeconds(timeout ?? 15) };
            GameSimFullPlanner.Validate(plan);
        }
        GameSimBenchmarkOptions? simulationBenchmark = null;
        if (operation == CliOperation.GameSimBenchmark)
        {
            simulationBenchmark = new() { Threads = threads, Duration = TimeSpan.FromSeconds(simDuration), Timeout = TimeSpan.FromSeconds(timeout ?? 10), AllowCpuLoad = allow };
            GameSimBenchmarkRunner.Validate(simulationBenchmark);
        }
        return new(operation, json, output, options, device, html, memory, simulation, plan, simulationBenchmark);
    }

    private static string Value(string[] args, ref int index)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(L.T("Valor de argumento ausente."));
        return args[index];
    }
    private static int Number(string[] args, ref int index) => int.TryParse(Value(args, ref index), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ArgumentException(L.T("Use um número inteiro não negativo."));
}
