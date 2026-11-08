// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Linux counterpart of the original's "CORRIGIR" (which installed the PawnIO driver): explain why a CPU reading is missing and show
// fix it with one click: a fixed root script built only from whitelisted values runs through polkit (pkexec), which asks for the
// password in the system's own dialog (maintainer decision 2026-11-06: users must not have to type commands). The command stays visible.
namespace Ilmenyx.Core;

public enum CpuPowerAccess { Available, NotPresent, NoPermission, Unknown }

/// <param name="Apply">Fixed root script run on one click through polkit (pkexec): the system asks for the password, Ilmenyx never sees it.</param>
/// <param name="Undo">Fixed script that reverts <paramref name="Apply"/>.</param>
public sealed record SensorFix(string Title, string Why, IReadOnlyList<(string Label, string Command)> Commands, string? Warning = null, string? Apply = null, string? Undo = null, string? ApplyLabel = null);

public static class SensorHelp
{
    /// <summary>Kernel module that exposes the CPU temperature for this vendor string (/proc/cpuinfo vendor_id or model name).</summary>
    public static string TemperatureModule(string? cpu) => (cpu ?? "").Contains("AMD", StringComparison.OrdinalIgnoreCase) || (cpu ?? "").Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ? "k10temp" : "coretemp";

    /// <summary>
    /// One root script for several fixes, so the first-run screen asks for the password once. Each fix runs in its own subshell and a failure
    /// does not stop the next one; the exit code is 0 only if all of them worked. Only the fixed <see cref="SensorFix.Apply"/> scripts are used.
    /// </summary>
    public static string? Combined(IEnumerable<SensorFix> fixes)
    {
        var scripts = fixes.Select(f => f.Apply).OfType<string>().Where(s => s.Length > 0).ToList();
        if (scripts.Count == 0) return null;
        return scripts.Count == 1 ? scripts[0] : "rc=0; " + string.Concat(scripts.Select(s => "(" + s + ") || rc=1; ")) + "exit $rc";
    }

    public static IReadOnlyList<SensorFix> Build(bool cpuTemperatureMissing, CpuPowerAccess power, string? cpuModel, string? raplZone, bool virtualMachine)
    {
        var fixes = new List<SensorFix>();
        if (cpuTemperatureMissing)
        {
            if (virtualMachine)
                fixes.Add(new("Temperatura da CPU", "Este Linux roda numa máquina virtual: o hipervisor não repassa os sensores da CPU. Não há o que corrigir aqui; rode o Ilmenyx direto no PC.", []));
            else
            {
                string module = TemperatureModule(cpuModel); // whitelisted: k10temp or coretemp
                fixes.Add(new("Temperatura da CPU", $"O módulo do kernel que lê o sensor da sua CPU ({module}) não está carregado.",
                    [("Carregar agora (até reiniciar)", $"sudo modprobe {module}"),
                     ("Carregar sempre ao ligar o PC", $"echo {module} | sudo tee /etc/modules-load.d/ilmenyx-{module}.conf")],
                    Apply: $"modprobe {module} && printf '%s\\n' {module} > /etc/modules-load.d/ilmenyx-{module}.conf",
                    Undo: $"rm -f /etc/modules-load.d/ilmenyx-{module}.conf", ApplyLabel: "ATIVAR SENSOR"));
            }
        }
        string zone = raplZone is { Length: > 0 } z ? z.Replace("/sys/devices/virtual/powercap/intel-rapl/", "/sys/class/powercap/") : "/sys/class/powercap/intel-rapl:0";
        if (!zone.StartsWith("/sys/class/powercap/", StringComparison.Ordinal)) zone = "/sys/class/powercap/" + Path.GetFileName(zone);
        // Only a plain powercap zone name may reach a root script.
        if (!System.Text.RegularExpressions.Regex.IsMatch(zone, @"^/sys/class/powercap/intel-rapl:[0-9]{1,2}$")) zone = "/sys/class/powercap/intel-rapl:0";
        switch (power)
        {
            case CpuPowerAccess.NoPermission:
                fixes.Add(new("Consumo da CPU (W)",
                    "O contador de energia da CPU (RAPL) existe, mas o kernel só deixa o root ler. O Ilmenyx não roda como root; para ver o consumo, libere a leitura para os usuários.",
                    [("Liberar até reiniciar", $"sudo chmod o+r {zone}/energy_uj"),
                     ("Liberar sempre ao ligar o PC", "echo 'ACTION==\"add\", SUBSYSTEM==\"powercap\", KERNEL==\"intel-rapl:*\", RUN+=\"/bin/chmod o+r /sys%p/energy_uj\"' | sudo tee /etc/udev/rules.d/90-ilmenyx-rapl.rules"),
                     ("Desfazer", "sudo rm -f /etc/udev/rules.d/90-ilmenyx-rapl.rules && sudo chmod o-r " + zone + "/energy_uj")],
                    "O kernel fechou essa leitura em 2020 porque o consumo pode vazar informação para outros programas (ataque PLATYPUS, CVE-2020-8694). Num PC pessoal o risco é baixo; num computador compartilhado ou servidor, não libere.",
                    // Immediate chmod plus a udev rule, so it also holds after a reboot (the zones are created by udev at boot).
                    Apply: $"chmod o+r {zone}/energy_uj && printf '%s\\n' 'ACTION==\"add\", SUBSYSTEM==\"powercap\", KERNEL==\"intel-rapl:*\", RUN+=\"/bin/chmod o+r /sys%p/energy_uj\"' > /etc/udev/rules.d/90-ilmenyx-rapl.rules",
                    Undo: $"rm -f /etc/udev/rules.d/90-ilmenyx-rapl.rules /etc/tmpfiles.d/ilmenyx-rapl.conf; chmod o-r {zone}/energy_uj",
                    ApplyLabel: "LIBERAR CONSUMO DA CPU"));
                break;
            case CpuPowerAccess.NotPresent:
                fixes.Add(new("Consumo da CPU (W)", virtualMachine
                    ? "Máquinas virtuais não expõem o contador de energia da CPU."
                    : "O kernel não expõe o contador de energia (RAPL) desta CPU. Em CPUs Intel e AMD Zen ele vem do módulo intel_rapl_msr.",
                    virtualMachine ? [] : [("Carregar o módulo (até reiniciar)", "sudo modprobe intel_rapl_msr")],
                    Apply: virtualMachine ? null : "modprobe intel_rapl_msr", ApplyLabel: virtualMachine ? null : "CARREGAR MÓDULO"));
                break;
        }
        return fixes;
    }
}
