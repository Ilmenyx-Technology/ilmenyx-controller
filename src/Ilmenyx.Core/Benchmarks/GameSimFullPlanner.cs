// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Runtime.InteropServices;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core.Benchmarks;

/// <summary>Preflight specification; deliberately cannot allocate an engine or execute a Step.</summary>
public static class GameSimFullPlanner
{
    public static void Validate(GameSimFullPlanOptions options)
    {
        if (options.Threads is < 1 or > 64) throw new ArgumentException(L.T("Plano GameSim permite 1 a 64 workers."));
        if (options.Duration < TimeSpan.FromSeconds(1) || options.Duration > TimeSpan.FromSeconds(10))
            throw new ArgumentException(L.T("Duração planejada deve ser de 1 a 10 segundos."));
        if (options.Timeout < options.Duration + TimeSpan.FromSeconds(3) || options.Timeout > TimeSpan.FromSeconds(15))
            throw new ArgumentException(L.T("Timeout planejado deve cobrir warmup de 2 s, duração e margem de 1 s, no máximo 15 s."));
    }
    public static GameSimFullPlan Create(GameSimFullPlanOptions options)
    {
        Validate(options);
        return new("1.0", "gamesim-full-preflight", "not-executed", true,
            "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6", 30_000, 1000, 64, 1 / 60f, 12345,
            options.Threads, options.Threads * 4, 2, options.Duration.TotalSeconds, options.Timeout.TotalSeconds,
            200_000, null, L.T("measuredFrames / measuredElapsedSeconds (experimental simulation FPS)"),
            L.T("1000 / sortedFrameMilliseconds[min(n-1, floor(n*0.99))]; not the mean of the slowest 1%"),
            "not-validated", RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            [L.T("Runner experimental implementado, mas execução completa real NÃO TESTADA; este plano não mede FPS/score."),
             L.T("Validar matemática em 30.000 entidades, fórmula de FPS/p99, timeout/cancelamento e memória limitada."),
             L.T("Executar carga curta somente com autorização explícita; restrição anterior de stress continua vigente."),
             L.T("Comparar Windows em máquina/VM equivalente antes de alegar comparabilidade.")],
            [L.T("Warmup por relógio avança estado variável; não reiniciar mundo/RNG após aquecimento se preservar método original."),
             L.T("Limites implementados no runner experimental; o caminho de carga real continua sem validação no host."),
             L.T("No original, Score=AvgFps; não representa FPS de jogo real nem desempenho de GPU."),
             L.T("Timeout é cooperativo entre Steps completos, não interrupção instantânea."),
             L.T("Frame times limitados a 200.000; limite atingido suprime métricas, sem descartar amostras silenciosamente."),
             L.T("Sem monitoramento/proteção térmica, afinidade ou controle de energia implementados.")]);
    }
}
