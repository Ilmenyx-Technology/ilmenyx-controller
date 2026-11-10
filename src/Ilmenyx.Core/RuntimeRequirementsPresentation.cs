// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
namespace Ilmenyx.Core;

/// <summary>Fixed textual diagnostics, safe before ICU initialization. No cultural formatting.</summary>
public static class RuntimeRequirementsPresentation
{
    public static int Write(RuntimeRequirements requirements, TextWriter output)
    {
        output.WriteLine("Ilmenyx Controller doctor - by Ilmenyx Technology");
        output.WriteLine(L.T("Processo .NET Linux ativo. Sem inventario, SMART, sensores ou benchmarks."));
        WriteLibrary(output, "glibc (libc.so.6)", requirements.Glibc);
        WriteLibrary(output, "ICU (uc/i18n)", requirements.Icu);
        output.WriteLine(requirements.InvariantMode ? L.T("Globalizacao: invariant solicitado explicitamente.") : L.T("Globalizacao: modo padrao; requer ICU compativel."));
        WriteLibrary(output, "fontconfig", requirements.Fontconfig);
        WriteLibrary(output, "X11", requirements.X11);
        WriteLibrary(output, "EGL", requirements.Egl);
        output.WriteLine(requirements.X11Configured ? L.T("DISPLAY: definida.") : L.T("DISPLAY: ausente."));
        output.WriteLine(requirements.WaylandConfigured ? L.T("WAYLAND_DISPLAY: definida.") : L.T("WAYLAND_DISPLAY: ausente."));
        output.WriteLine(L.T("Biblioteca carregavel e variavel definida nao certificam ABI, fontes, driver ou conexao grafica."));
        output.WriteLine(L.T("GUI nao testada por este comando. Invariant nao certifica globalizacao padrao."));
        bool ready = requirements.CliReady;
        output.WriteLine(ready ? L.T("Requisitos basicos CLI: encontrados; codigo 0.") : L.T("Requisitos basicos CLI: ausentes; codigo 5."));
        return ready ? 0 : 5;
    }

    private static void WriteLibrary(TextWriter output, string name, bool loaded) =>
        output.WriteLine(name + (loaded ? L.T(": carregavel.") : L.T(": nao encontrada/carregavel.")));
}
