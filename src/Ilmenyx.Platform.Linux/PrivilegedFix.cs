// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Runs one of the fixed scripts of <see cref="Ilmenyx.Core.SensorHelp"/> as root through polkit (pkexec): only after the user's click
/// and confirmation, with the system's own password dialog (Ilmenyx never sees the password). No other script reaches this method.
/// </summary>
public static class PrivilegedFix
{
    public static bool Available => BoundedProcess.FindSystem("pkexec") is not null;

    /// <summary>(ok, message). pkexec exit 126 = the dialog was dismissed, 127 = not authorized.</summary>
    public static async Task<(bool Ok, string Message)> RunAsync(string script, CancellationToken ct = default)
    {
        if (!Available) return (false, "O pedido de senha do sistema (pkexec/polkit) não está instalado.");
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("pkexec", ["/bin/sh", "-c", script], TimeSpan.FromMinutes(3), 8192, ct);
            return code switch
            {
                0 => (true, "Pronto."),
                126 => (false, "Cancelado: a senha não foi informada."),
                127 => (false, "O sistema não autorizou (sua conta precisa ser de administrador)."),
                _ => (false, "Falhou (código " + code + "): " + output.Trim()),
            };
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return (false, "Falhou: " + e.Message); }
    }
}
