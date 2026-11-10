// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>Status and state names for text output. JSON keeps the raw values (enum names, "available"…); text shows these.</summary>
public static class StatusText
{
    /// <summary>Reading/snapshot status as stored in the contracts.</summary>
    public static string Of(string status) => status switch
    {
        "available" => L.T("disponível"),
        "partial" => L.T("parcial"),
        "unavailable" => L.T("indisponível"),
        "unknown" => L.T("desconhecido"),
        _ => status
    };

    /// <summary>Run results shared by the benchmarks and validations (Completed, Cancelled, TimedOut…).</summary>
    public static string Of(Enum status) => status.ToString() switch
    {
        "Completed" => L.T("concluído"),
        "Cancelled" => L.T("cancelado"),
        "TimedOut" => L.T("tempo esgotado"),
        "Failed" => L.T("falhou"),
        "Unavailable" => L.T("indisponível"),
        "SampleLimitReached" => L.T("limite de amostras atingido"),
        "InvalidTiming" => L.T("medição de tempo inválida"),
        var name => name
    };

    public static string Of(ZramState state) => state switch
    {
        ZramState.Unsupported => L.T("sem suporte"),
        ZramState.Unknown => L.T("não identificado"),
        ZramState.ActiveOurs => L.T("ativo (configurado pelo Ilmenyx)"),
        ZramState.Active => L.T("ativo"),
        ZramState.ConfiguredInactive => L.T("configurado, mas inativo"),
        ZramState.ForeignConfig => L.T("configurado por outro programa"),
        ZramState.NotNeeded => L.T("não necessário"),
        ZramState.Recommended => L.T("recomendado"),
        _ => state.ToString()
    };

    public static string Of(NvidiaDriverState state) => state switch
    {
        NvidiaDriverState.NoNvidiaGpu => L.T("sem placa NVIDIA"),
        NvidiaDriverState.Unsupported => L.T("sem suporte"),
        NvidiaDriverState.Working => L.T("funcionando"),
        NvidiaDriverState.LoadedButSmiFails => L.T("carregado, mas o nvidia-smi falha"),
        NvidiaDriverState.NeedsReboot => L.T("precisa reiniciar"),
        NvidiaDriverState.Broken => L.T("com defeito"),
        NvidiaDriverState.NotInstalled => L.T("não instalado"),
        _ => state.ToString()
    };
}
