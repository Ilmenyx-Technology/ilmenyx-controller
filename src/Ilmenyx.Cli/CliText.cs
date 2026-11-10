// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
namespace Ilmenyx.Cli;

/// <summary>Command syntax in help and usage lines: commands and flags stay as typed, the uppercase placeholders follow the language.</summary>
public static class CliText
{
    public static string Syntax(string text) => text
        .Replace("RELATORIO_SMARTCTL", L.T("RELATORIO_SMARTCTL"), StringComparison.Ordinal)
        .Replace("ARQUIVO", L.T("ARQUIVO"), StringComparison.Ordinal)
        .Replace("PASTA", L.T("PASTA"), StringComparison.Ordinal)
        .Replace("TEXTO", L.T("TEXTO"), StringComparison.Ordinal)
        .Replace("NOME", L.T("NOME"), StringComparison.Ordinal);
}
