// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Contracts;
namespace Ilmenyx.Core;

/// <summary>Fixed textual diagnostics, safe before ICU initialization. No cultural formatting.</summary>
public static class RuntimeRequirementsPresentation
{
    public static int Write(RuntimeRequirements requirements, TextWriter output)
    {
        output.WriteLine("Ilmenyx doctor - by Bismuth Technology");
        output.WriteLine("Processo .NET Linux ativo. Sem inventario, SMART, sensores ou benchmarks.");
        WriteLibrary(output, "glibc (libc.so.6)", requirements.Glibc);
        WriteLibrary(output, "ICU (uc/i18n)", requirements.Icu);
        output.WriteLine(requirements.InvariantMode ? "Globalizacao: invariant solicitado explicitamente." : "Globalizacao: modo padrao; requer ICU compativel.");
        WriteLibrary(output, "fontconfig", requirements.Fontconfig);
        WriteLibrary(output, "X11", requirements.X11);
        WriteLibrary(output, "EGL", requirements.Egl);
        output.WriteLine(requirements.X11Configured ? "DISPLAY: definida." : "DISPLAY: ausente.");
        output.WriteLine(requirements.WaylandConfigured ? "WAYLAND_DISPLAY: definida." : "WAYLAND_DISPLAY: ausente.");
        output.WriteLine("Biblioteca carregavel e variavel definida nao certificam ABI, fontes, driver ou conexao grafica.");
        output.WriteLine("GUI nao testada por este comando. Invariant nao certifica globalizacao padrao.");
        bool ready = requirements.CliReady;
        output.WriteLine(ready ? "Requisitos basicos CLI: encontrados; codigo 0." : "Requisitos basicos CLI: ausentes; codigo 5.");
        return ready ? 0 : 5;
    }

    private static void WriteLibrary(TextWriter output, string name, bool loaded) =>
        output.WriteLine(name + (loaded ? ": carregavel." : ": nao encontrada/carregavel."));
}
