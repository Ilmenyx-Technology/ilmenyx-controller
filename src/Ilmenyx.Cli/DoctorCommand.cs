// New Ilmenyx additions: see LICENSING.md; reused components retain their terms.
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Cli;

/// <summary>Text-only diagnostics: no hardware provider, UI connection or cultural formatting.</summary>
public static class DoctorCommand
{
    public static int Run(LinuxRuntimeDependencies dependencies, TextWriter output,
        bool invariantMode, bool x11Configured, bool waylandConfigured)
    {
        var requirements = dependencies.Inspect();
        output.WriteLine("Ilmenyx doctor - by Bismuth Technology");
        output.WriteLine("Processo .NET Linux ativo. Sem inventario, SMART, sensores ou benchmarks.");
        WriteLibrary(output, "glibc (libc.so.6)", requirements.Glibc);
        WriteLibrary(output, "ICU (uc/i18n)", requirements.Icu);
        output.WriteLine(invariantMode ? "Globalizacao: invariant solicitado explicitamente." : "Globalizacao: modo padrao; requer ICU compativel.");
        WriteLibrary(output, "fontconfig", requirements.Fontconfig);
        WriteLibrary(output, "X11", requirements.X11);
        WriteLibrary(output, "EGL", requirements.Egl);
        output.WriteLine(x11Configured ? "DISPLAY: definida." : "DISPLAY: ausente.");
        output.WriteLine(waylandConfigured ? "WAYLAND_DISPLAY: definida." : "WAYLAND_DISPLAY: ausente.");
        output.WriteLine("Biblioteca carregavel e variavel definida nao certificam ABI, fontes, driver ou conexao grafica.");
        output.WriteLine("GUI nao testada por este comando. Invariant nao certifica globalizacao padrao.");
        bool ready = requirements.Glibc && (invariantMode || requirements.Icu);
        output.WriteLine(ready ? "Requisitos basicos CLI: encontrados; codigo 0." : "Requisitos basicos CLI: ausentes; codigo 5.");
        return ready ? 0 : 5;
    }

    private static void WriteLibrary(TextWriter output, string name, bool loaded) =>
        output.WriteLine(name + (loaded ? ": carregavel." : ": nao encontrada/carregavel."));
}
