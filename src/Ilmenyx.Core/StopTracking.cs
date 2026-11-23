// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>Text of the "Parar acompanhamento" confirmation: lists only what really stops for the launch that is being followed.</summary>
public static class StopTracking
{
    /// <param name="launch">The profile the launch started with.</param>
    /// <param name="following">False when only the achievement watch is left (the launch follow-up already ended).</param>
    /// <param name="measured">This launch is being measured (MangoHud ticked, or Medir FPS with MangoHud available).</param>
    public static string Explain(string game, GameProfile launch, bool following, bool windowsManual, bool measured)
    {
        var lines = new List<string>();
        if (following)
        {
            if (measured) lines.Add(L.T("Esta partida deixa de ser medida: ela não entra no DESEMPENHO MEDIDO."));
            if (windowsManual) lines.Add(L.T("Os avisos de conquista deste jogo param."));
            if (launch.HighPerfPlan) lines.Add(L.T("O perfil de energia volta ao anterior agora, com o jogo ainda aberto."));
            if (launch.Helpers.Any(h => h.CloseWithGame)) lines.Add(L.T("Os programas auxiliares marcados para fechar com o jogo são fechados agora."));
            if (launch.MangoHud) lines.Add(L.T("O MangoHud continua aparecendo no jogo até ele fechar."));
            else if (measured) lines.Add(L.T("O MangoHud continua carregado no jogo, escondido, até ele fechar."));
        }
        else lines.Add(L.T("Os avisos de conquista deste jogo param."));
        lines.Add(L.T("O jogo não é fechado. Para o Ilmenyx voltar a acompanhar, feche o jogo e abra-o de novo pelo Jogar."));
        return L.T($"O Ilmenyx deixa de acompanhar {game}.") + "\n\n" + string.Join("\n", lines.Select(l => "• " + l));
    }
}
