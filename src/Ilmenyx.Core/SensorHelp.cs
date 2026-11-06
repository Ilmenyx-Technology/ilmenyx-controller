// New Ilmenyx additions: see LICENSING.md.
// Linux counterpart of the original's "CORRIGIR" (which installed the PawnIO driver): explain why a CPU reading is missing and show
// the command that fixes it. Commands that need root are only shown and copied, never run by Ilmenyx.
namespace Ilmenyx.Core;

public enum CpuPowerAccess { Available, NotPresent, NoPermission, Unknown }

public sealed record SensorFix(string Title, string Why, IReadOnlyList<(string Label, string Command)> Commands, string? Warning = null);

public static class SensorHelp
{
    /// <summary>Kernel module that exposes the CPU temperature for this vendor string (/proc/cpuinfo vendor_id or model name).</summary>
    public static string TemperatureModule(string? cpu) => (cpu ?? "").Contains("AMD", StringComparison.OrdinalIgnoreCase) || (cpu ?? "").Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ? "k10temp" : "coretemp";

    public static IReadOnlyList<SensorFix> Build(bool cpuTemperatureMissing, CpuPowerAccess power, string? cpuModel, string? raplZone, bool virtualMachine)
    {
        var fixes = new List<SensorFix>();
        if (cpuTemperatureMissing)
        {
            if (virtualMachine)
                fixes.Add(new("Temperatura da CPU", "Este Linux roda numa máquina virtual: o hipervisor não repassa os sensores da CPU. Não há o que corrigir aqui; rode o Ilmenyx direto no PC.", []));
            else
            {
                string module = TemperatureModule(cpuModel);
                fixes.Add(new("Temperatura da CPU", $"O módulo do kernel que lê o sensor da sua CPU ({module}) não está carregado.",
                    [("Carregar agora (até reiniciar)", $"sudo modprobe {module}"),
                     ("Carregar sempre ao ligar o PC", $"echo {module} | sudo tee /etc/modules-load.d/ilmenyx-{module}.conf")]));
            }
        }
        string zone = raplZone is { Length: > 0 } z ? z.Replace("/sys/devices/virtual/powercap/intel-rapl/", "/sys/class/powercap/") : "/sys/class/powercap/intel-rapl:0";
        if (!zone.StartsWith("/sys/class/powercap/", StringComparison.Ordinal)) zone = "/sys/class/powercap/" + Path.GetFileName(zone);
        switch (power)
        {
            case CpuPowerAccess.NoPermission:
                fixes.Add(new("Consumo da CPU (W)",
                    "O contador de energia da CPU (RAPL) existe, mas o kernel só deixa o root ler. O Ilmenyx não roda como root; para ver o consumo, libere a leitura para os usuários.",
                    [("Liberar até reiniciar", $"sudo chmod o+r {zone}/energy_uj"),
                     ("Liberar sempre ao ligar o PC", $"echo 'z {zone}/energy_uj 0444 - - -' | sudo tee /etc/tmpfiles.d/ilmenyx-rapl.conf && sudo systemd-tmpfiles --create /etc/tmpfiles.d/ilmenyx-rapl.conf"),
                     ("Desfazer", "sudo rm -f /etc/tmpfiles.d/ilmenyx-rapl.conf && sudo chmod o-r " + zone + "/energy_uj")],
                    "O kernel fechou essa leitura em 2020 porque o consumo pode vazar informação para outros programas (ataque PLATYPUS, CVE-2020-8694). Num PC pessoal o risco é baixo; num computador compartilhado ou servidor, não libere."));
                break;
            case CpuPowerAccess.NotPresent:
                fixes.Add(new("Consumo da CPU (W)", virtualMachine
                    ? "Máquinas virtuais não expõem o contador de energia da CPU."
                    : "O kernel não expõe o contador de energia (RAPL) desta CPU. Em CPUs Intel e AMD Zen ele vem do módulo intel_rapl_msr.",
                    virtualMachine ? [] : [("Carregar o módulo (até reiniciar)", "sudo modprobe intel_rapl_msr")]));
                break;
        }
        return fixes;
    }
}
