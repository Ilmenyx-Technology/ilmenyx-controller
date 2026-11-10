// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Driver NVIDIA" tab: from a read-only diagnosis, a plan in plain language (what will be done and why, with the risks) and the FIXED root
// scripts that carry it out through polkit. Scripts are built only from values validated against lists/regexes (branch from the table,
// package names, kernel version); no free text, report or network value reaches them (AGENTS.md, "Correções de sensores com um clique").
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public enum NvidiaDriverState { NoNvidiaGpu, Unsupported, Working, LoadedButSmiFails, NeedsReboot, Broken, NotInstalled }

public enum NvidiaActionKind { Install, Repair, Update, Revert }

/// <param name="Steps">What will be done, in plain language, in order.</param>
/// <param name="Script">The fixed root script (shown to technicians and run by pkexec /bin/sh -c).</param>
/// <param name="SimulationArguments">apt-get arguments checked first with `apt-get -s` (no root); null when the family has no simulation.</param>
/// <param name="AllowedRemovals">Packages this action may remove besides the NVIDIA driver packages (repair: the broken kernel only).</param>
/// <param name="Tested">Exercised for real on a machine (Ubuntu/Mint apt path); false = NÃO TESTADO.</param>
/// <param name="Blocked">Why the action cannot run by one click (guidance only); null when it can.</param>
public sealed record NvidiaAction(NvidiaActionKind Kind, string Title, string Button, IReadOnlyList<string> Steps, IReadOnlyList<string> Risks, string Script,
    IReadOnlyList<string>? SimulationArguments, IReadOnlyList<string> AllowedRemovals, bool Tested, string? Blocked = null);

public sealed record NvidiaPlan(NvidiaDriverState State, string Headline, string Summary, IReadOnlyList<string> Findings, IReadOnlyList<NvidiaAction> Actions, string? Guidance = null);

public static partial class NvidiaScripts
{
    [GeneratedRegex(@"^[a-z0-9][a-z0-9+.\-]{1,90}$", RegexOptions.CultureInvariant)] private static partial Regex PackageRegex();
    [GeneratedRegex(@"^[0-9]{1,2}\.[0-9]{1,3}(\.[0-9]{1,4})?([-.+_][A-Za-z0-9]{1,24}){0,6}$", RegexOptions.CultureInvariant)] private static partial Regex KernelRegex();
    [GeneratedRegex(@"^[0-9]{1,2}\.[0-9]{1,3}\.[0-9]{1,4}-[0-9]{1,4}-[a-z0-9]{2,20}$", RegexOptions.CultureInvariant)] private static partial Regex UbuntuKernelRegex();
    [GeneratedRegex(@"^((lib)?nvidia-[a-z0-9.+\-]*-[0-9]{3}([-.][a-z0-9.+\-]+)?|xserver-xorg-video-nvidia-[0-9]{3}([-.][a-z0-9.+\-]+)?|linux-(modules|objects|signatures)-nvidia-[a-z0-9.+\-]+)$", RegexOptions.CultureInvariant)] private static partial Regex NvidiaPackageRegex();
    [GeneratedRegex(@"^linux-modules-nvidia-[0-9]{3}(-open|-server|-server-open)?-(generic|lowlatency)(-hwe-[0-9]{2}\.[0-9]{2})?$", RegexOptions.CultureInvariant)] private static partial Regex PrebuiltMetaRegex();

    /// <summary>Debian package name (lowercase, digits, + . -), never an option ("-x") or shell text.</summary>
    public static bool ValidPackage(string name) => PackageRegex().IsMatch(name);
    public static bool ValidKernel(string kernel) => KernelRegex().IsMatch(kernel);
    public static bool ValidBranch(string? branch) => branch is { Length: 3 } && branch.All(char.IsAsciiDigit) && NvidiaGenerations.Branches.Contains(branch);
    /// <summary>A package of a numbered NVIDIA driver branch or a prebuilt NVIDIA module package (what the undo removes). Never nvidia-prime/-settings.</summary>
    public static bool IsNvidiaPackage(string name) => ValidPackage(name) && NvidiaPackageRegex().IsMatch(name);
    public static bool IsPrebuiltMeta(string name) => PrebuiltMetaRegex().IsMatch(name);
    /// <summary>"6.14.0-37-generic" → "6.14.0-37" (the part shared by every package of that kernel).</summary>
    public static string KernelBase(string kernel) => UbuntuKernelRegex().IsMatch(kernel) ? kernel[..kernel.LastIndexOf('-')] : kernel;
    public static bool IsUbuntuKernel(string kernel) => UbuntuKernelRegex().IsMatch(kernel);

    /// <summary>The kernel itself (image, modules, headers) of <paramref name="kernel"/>: never removed for the running kernel.</summary>
    public static bool IsKernelCore(string package, string kernel)
    {
        string k = Regex.Escape(kernel), b = Regex.Escape(KernelBase(kernel));
        return Regex.IsMatch(package, $@"^linux-(image|image-unsigned|modules|modules-extra|headers)-{k}$") || Regex.IsMatch(package, $@"^linux-(hwe-[0-9.]+-)?headers-{b}$");
    }

    /// <summary>Packages that belong to kernel <paramref name="kernel"/> (image, modules, prebuilt NVIDIA objects, headers).</summary>
    public static bool BelongsToKernel(string package, string kernel)
    {
        if (!IsUbuntuKernel(kernel)) return false;
        string k = Regex.Escape(kernel), b = Regex.Escape(KernelBase(kernel));
        return Regex.IsMatch(package, $@"^linux-(image|image-unsigned|modules|modules-extra|headers|main-modules-[a-z0-9]+|modules-nvidia-[0-9]{{3}}(-open|-server|-server-open)?|objects-nvidia-[0-9]{{3}}(-open|-server|-server-open)?|signatures-nvidia)-{k}$")
            || Regex.IsMatch(package, $@"^linux-(hwe-[0-9.]+-)?headers-{b}$");
    }

    internal static string Quote(string value)
    {
        // Every value was validated by the regexes above (no quote, space or shell character); quoting is defence in depth.
        if (value.Contains('\'') || value.Any(char.IsWhiteSpace)) throw new ArgumentException(L.T("Valor inválido para script: ") + value);
        return "'" + value + "'";
    }

    private static string Words(IEnumerable<string> values) => string.Join(' ', values.Select(Quote));

    /// <summary>Apt script: re-checks with apt-get -s as root (state may change between the click and the run), runs apt-get, verifies, PRIME.</summary>
    internal static string Apt(IReadOnlyList<string> arguments, string guardPattern, string kernel, bool dkmsCheck, bool primeOnDemand, string doneMessage)
    {
        if (!arguments.All(a => a is "install" or "purge" or "--purge" || ValidPackage(a.TrimEnd('-')))) throw new ArgumentException(L.T("Argumento inválido para o apt."));
        if (!ValidKernel(kernel)) throw new ArgumentException(L.T("Kernel inválido."));
        string args = string.Join(' ', arguments.Select(Quote));
        var s = new List<string>
        {
            "set -eu", L.T("export DEBIAN_FRONTEND=noninteractive"), "export LC_ALL=C",
            "echo '== Ilmenyx 1/4: simulação de segurança (apt-get -s): nenhum kernel novo, o kernel em uso fica'",
            $"SIM=$(apt-get -s {args} 2>&1) || {{ printf '%s\\n' \"$SIM\"; echo 'ABORTADO: a simulação do apt falhou; nada foi alterado.'; exit 3; }}",
            $"if printf '%s\\n' \"$SIM\" | grep -Eq '{guardPattern}'; then printf '%s\\n' \"$SIM\" | grep -E '^(Inst|Remv|Purg) linux-' || true; echo 'ABORTADO: o apt quer instalar ou remover um kernel; nada foi alterado.'; exit 3; fi",
            $"echo '== Ilmenyx 2/4: apt-get -y {args.Replace("'", "")}'",
            $"apt-get -y {args}",
            "echo '== Ilmenyx 3/4: verificação'",
        };
        if (dkmsCheck)
        {
            s.Add($"if command -v dkms >/dev/null 2>&1; then dkms status -k {Quote(kernel)} || true; fi");
            s.Add($"if ls /lib/modules/{Quote(kernel)}/updates/dkms/nvidia.ko* >/dev/null 2>&1 || find /lib/modules/{Quote(kernel)} -name 'nvidia.ko*' 2>/dev/null | grep -q .; then echo 'OK: módulo nvidia presente para o kernel {kernel}.'; else echo 'ATENÇÃO: módulo nvidia não encontrado para o kernel {kernel}.'; exit 4; fi");
        }
        else s.Add("dpkg -l | grep -E '^.. +(lib)?nvidia-[a-z0-9.+-]*-[0-9]{3}' || echo 'OK: nenhum pacote do driver NVIDIA restante.'");
        s.Add("echo '== Ilmenyx 4/4: PRIME'");
        s.Add(primeOnDemand ? "if command -v prime-select >/dev/null 2>&1; then prime-select on-demand || true; prime-select query || true; fi" : "echo 'PRIME: sem alteração.'");
        s.Add($"echo '== Ilmenyx: {doneMessage}'");
        return string.Join('\n', s) + "\n";
    }

    internal static string GuardInstall(string kernel) =>
        $"^Inst linux-(image|modules|objects|signatures|main-modules)-|^(Remv|Purg) linux-image-|^(Remv|Purg) linux-(modules|modules-extra|headers)-{EscapeEre(kernel)}( |$)";
    internal static string GuardRepair(string kernel) =>
        $"^Inst linux-(image|modules|objects|signatures|main-modules)-|^(Remv|Purg) linux-(image|image-unsigned|modules|modules-extra|headers)-{EscapeEre(kernel)}( |$)";
    private static string EscapeEre(string kernel) => kernel.Replace(".", "\\.").Replace("+", "\\+");
}

public static class NvidiaPlanner
{
    public static NvidiaDriverState Evaluate(NvidiaDiagnosis d)
    {
        if (d.Gpus.Count == 0) return NvidiaDriverState.NoNvidiaGpu;
        var gpu = d.Primary!;
        bool nvidiaBound = gpu.KernelDriver.StartsWith("nvidia", StringComparison.Ordinal);
        if (nvidiaBound && d.LoadedVersion is not null) return d.SmiVersion is not null || !d.SmiInstalled && !d.Distro.UsesApt ? NvidiaDriverState.Working : NvidiaDriverState.LoadedButSmiFails;
        if (gpu.Support.Branch is null) return NvidiaDriverState.Unsupported;
        if (d.Distro.UsesApt)
        {
            if (NeedsRepair(d)) return NvidiaDriverState.Broken;
            if (d.InstalledDriver is not null)
                return d.ModuleReadyForRunningKernel ? NvidiaDriverState.NeedsReboot : NvidiaDriverState.Broken;
            return NvidiaDriverState.NotInstalled;
        }
        return d.ModuleReadyForRunningKernel ? NvidiaDriverState.NeedsReboot : NvidiaDriverState.NotInstalled;
    }

    private static bool NeedsRepair(NvidiaDiagnosis d) =>
        d.BrokenPackages.Any(p => p.Name.StartsWith("linux-", StringComparison.Ordinal) || NvidiaScripts.IsNvidiaPackage(p.Name)) || SuspectKernels(d).Count > 0;

    /// <summary>
    /// Installed kernels other than the running one that break the NVIDIA driver: a half-configured package of that kernel (the prebuilt
    /// NVIDIA modules failed), or headers present but incomplete (objtool/module.lds missing, so DKMS cannot build). Never the running kernel.
    /// </summary>
    public static IReadOnlyList<string> SuspectKernels(NvidiaDiagnosis d)
    {
        var list = new List<string>();
        foreach (var k in d.Kernels)
        {
            if (k.Kernel == d.RunningKernel || !NvidiaScripts.IsUbuntuKernel(k.Kernel) || NvidiaScripts.KernelBase(k.Kernel) == NvidiaScripts.KernelBase(d.RunningKernel)) continue;
            bool brokenPackage = d.BrokenPackages.Any(p => NvidiaScripts.BelongsToKernel(p.Name, k.Kernel));
            bool incompleteHeaders = k.Present && !k.Complete;
            if (brokenPackage || incompleteHeaders) list.Add(k.Kernel);
        }
        return list;
    }

    private static string DriverPackage(string branch, bool open) => "nvidia-driver-" + branch + (open ? "-open" : "");
    private static string DkmsPackage(string branch, bool open) => "nvidia-dkms-" + branch + (open ? "-open" : "");

    /// <summary>The packages to install for the running kernel (DKMS, never the prebuilt per-kernel modules that pull another kernel).</summary>
    public static IReadOnlyList<string> UbuntuPackages(string branch, bool open, string kernel)
    {
        if (!NvidiaScripts.ValidBranch(branch)) throw new ArgumentException(L.T("Ramo fora da tabela: ") + branch);
        if (!NvidiaScripts.IsUbuntuKernel(kernel)) throw new ArgumentException(L.T("Kernel sem pacote de cabeçalhos da distribuição: ") + kernel);
        return [DkmsPackage(branch, open), DriverPackage(branch, open), "linux-headers-" + kernel];
    }

    public static NvidiaPlan Plan(NvidiaDiagnosis d)
    {
        var state = Evaluate(d);
        var findings = Findings(d, state);
        if (state == NvidiaDriverState.NoNvidiaGpu)
            return new(state, L.T("Nenhuma placa NVIDIA neste computador"), L.T("Esta aba instala e cuida do driver de placas NVIDIA. Não encontramos nenhuma no barramento PCI."), findings, []);
        var gpu = d.Primary!; var support = gpu.Support;
        string gpuText = $"{NvidiaGenerations.MarketingName(gpu.Name)} ({NvidiaGenerations.Name(gpu.Generation)}{(gpu.Chip.Length > 0 ? " · " + gpu.Chip : "")})";
        var actions = new List<NvidiaAction>();
        string? guidance = null;
        var installed = d.InstalledDriver;
        bool anyDriverPackage = d.NvidiaPackages.Any();

        if (d.Distro.UsesApt && NvidiaScripts.IsUbuntuKernel(d.RunningKernel))
        {
            bool tested = d.Distro.Family == DistroFamily.Ubuntu;
            if (support.Branch is { } branch && d.Distro.Family == DistroFamily.Ubuntu)
            {
                bool open = support.Open != NvidiaOpenModules.Unsupported;
                switch (state)
                {
                    case NvidiaDriverState.NotInstalled:
                        actions.Add(UbuntuInstall(d, NvidiaActionKind.Install, branch, open, [], tested));
                        break;
                    case NvidiaDriverState.Broken:
                        actions.Add(UbuntuRepair(d, installed?.Branch is { } b && NvidiaScripts.ValidBranch(b) ? b : branch, installed?.Open ?? open, tested));
                        break;
                    case NvidiaDriverState.Working or NvidiaDriverState.LoadedButSmiFails or NvidiaDriverState.NeedsReboot:
                        if (installed is { } i)
                        {
                            bool otherBranch = i.Branch != branch && int.Parse(i.Branch) < int.Parse(branch);
                            bool newer = d.CandidateVersion is { } c && c != i.Version;
                            if (state != NvidiaDriverState.NeedsReboot && (otherBranch || newer))
                                actions.Add(UbuntuInstall(d, NvidiaActionKind.Update, branch, otherBranch ? open : i.Open, [], tested, otherBranch ? i.Branch : null));
                        }
                        // Working now, but a half-installed package or a broken extra kernel would fail at the next boot/update.
                        if (NeedsRepair(d)) actions.Add(UbuntuRepair(d, installed?.Branch is { } wb && NvidiaScripts.ValidBranch(wb) ? wb : branch, installed?.Open ?? open, tested));
                        break;
                }
            }
            else if (d.Distro.Family == DistroFamily.Debian && support.Branch is not null && state == NvidiaDriverState.NotInstalled)
                actions.Add(DebianInstall(d));
            if (anyDriverPackage) actions.Add(UbuntuRevert(d, tested));
        }
        else if (support.Branch is not null && state is NvidiaDriverState.NotInstalled)
        {
            var other = OtherFamilyInstall(d, gpu);
            if (other is not null) actions.Add(other);
        }
        if (!d.Distro.UsesApt && state is NvidiaDriverState.Working or NvidiaDriverState.NeedsReboot or NvidiaDriverState.LoadedButSmiFails && OtherFamilyRevert(d) is { } revert) actions.Add(revert);
        if (support.Branch is null) guidance = support.Note;
        else if (actions.Count == 0 && state is NvidiaDriverState.NotInstalled or NvidiaDriverState.Broken)
            guidance = d.Distro.Family == DistroFamily.Other
                ? L.T($"Sua distribuição ({d.Distro.Name}) não está na lista do Ilmenyx. Use o gerenciador de drivers dela e escolha o ramo {support.Branch}.")
                : NvidiaScripts.IsUbuntuKernel(d.RunningKernel) || !d.Distro.UsesApt ? L.T($"Sem plano automático para esta combinação. Use o gerenciador de drivers da distribuição e escolha o ramo {support.Branch}.")
                : L.T($"O kernel em uso ({d.RunningKernel}) não é um kernel da distribuição: não há pacote de cabeçalhos para ele. Inicie um kernel da distribuição e verifique de novo.");

        var (headline, summary) = state switch
        {
            NvidiaDriverState.Working => (L.T($"Funcionando · driver {d.SmiVersion ?? d.LoadedVersion}"), L.T($"{gpuText} usa o driver NVIDIA {d.SmiVersion ?? d.LoadedVersion}; o nvidia-smi responde.")),
            NvidiaDriverState.LoadedButSmiFails => (L.T("Driver carregado, mas o nvidia-smi não responde"), L.T($"O módulo NVIDIA {d.LoadedVersion} está carregado para {gpuText}, mas o nvidia-smi falhou ou não está instalado. Reiniciar costuma resolver depois de uma atualização.")),
            NvidiaDriverState.NeedsReboot => (L.T("Instalado · reinicie o computador"), L.T($"O driver NVIDIA está instalado e o módulo existe para o kernel {d.RunningKernel}, mas a placa ainda usa {d.KernelDriver}. Reinicie para ativar.")),
            NvidiaDriverState.Broken => (L.T("Instalação com problema · reparo disponível"), L.T($"Há pacotes do driver ou do kernel pela metade (ou um kernel sem cabeçalhos completos). {gpuText} não tem o driver NVIDIA funcionando.")),
            NvidiaDriverState.NotInstalled => (L.T($"Driver NVIDIA não instalado · em uso: {d.KernelDriver}"), L.T($"{gpuText}: o driver recomendado é o ramo {support.Branch}. {support.Note}")),
            NvidiaDriverState.Unsupported => (L.T("Sem driver NVIDIA suportado para esta placa"), $"{gpuText}: {support.Note}"),
            _ => ("", ""),
        };
        return new NvidiaPlan(state, headline, summary, findings, actions, guidance);
    }

    private static List<string> CommonRisks(NvidiaDiagnosis d, NvidiaBranchSupport support)
    {
        var risks = new List<string>();
        if (d.SecureBoot == SecureBootState.Enabled)
            risks.Add(L.T("Secure Boot ligado: o módulo montado pelo DKMS é assinado com uma chave local (MOK) que precisa ser registrada. Depois de instalar, rode no terminal `sudo mokutil --import /var/lib/shim-signed/mok/MOK.der` (crie uma senha de uso único), reinicie e escolha \"Enroll MOK\" na tela azul. Sem isso a placa continua no nouveau. (NÃO TESTADO nesta máquina, que tem o Secure Boot desligado.)"));
        if (d.SecureBoot == SecureBootState.Unknown) risks.Add(L.T("Não foi possível ler o estado do Secure Boot (mokutil ausente). Se ele estiver ligado, será preciso registrar uma chave MOK no próximo boot."));
        if (support.Generation == NvidiaGeneration.Kepler) risks.Add(L.T("Driver legado 470: a NVIDIA encerrou o suporte; a distribuição só aplica correções. Pode deixar de compilar em um kernel futuro."));
        else if (support.Legacy) risks.Add(L.T($"Ramo {support.Branch} legado: é o último que suporta {NvidiaGenerations.Name(support.Generation)}. Não troque por um ramo mais novo (590+), que não reconhece esta placa."));
        risks.Add(L.T("Baixa ~400 MB da internet e leva alguns minutos (monta o módulo para o kernel em uso). Não desligue o computador durante a instalação."));
        return risks;
    }

    private static NvidiaAction UbuntuInstall(NvidiaDiagnosis d, NvidiaActionKind kind, string branch, bool open, IReadOnlyList<string> purge, bool tested, string? fromBranch = null)
    {
        var pkgs = UbuntuPackages(branch, open, d.RunningKernel);
        var support = d.Primary!.Support;
        var steps = new List<string>
        {
            L.T($"Instalar {DriverPackage(branch, open)} (driver NVIDIA ramo {branch}{(open ? L.T(", módulos \"open\"") : "")}) e {DkmsPackage(branch, open)}: o DKMS monta o módulo para o kernel que está em uso ({d.RunningKernel})."),
            L.T($"Garantir os cabeçalhos do kernel em uso (linux-headers-{d.RunningKernel}), que o DKMS precisa para montar o módulo."),
            L.T("Não usar os módulos pré-compilados por kernel (linux-modules-nvidia-*): eles puxam um kernel novo, que foi o que falhou no caso real desta aba."),
        };
        if (fromBranch is not null) steps.Insert(0, L.T($"Trocar o ramo {fromBranch} instalado pelo {branch}: o apt remove os pacotes do ramo antigo."));
        if (d.Hybrid && d.PrimeSelectAvailable) steps.Add(L.T("Deixar o PRIME em \"on-demand\": a tela continua na placa integrada e a NVIDIA entra nos jogos/programas que pedirem (economiza bateria)."));
        steps.Add(L.T("Conferir no final (dkms status e arquivo do módulo) e pedir para você reiniciar."));
        string title = kind == NvidiaActionKind.Update ? (fromBranch is null ? L.T($"Atualizar o driver NVIDIA {branch}") : L.T($"Atualizar para o ramo {branch}")) : L.T($"Instalar o driver NVIDIA {branch}");
        string script = NvidiaScripts.Apt(["install", .. pkgs], NvidiaScripts.GuardInstall(d.RunningKernel), d.RunningKernel, true, d.Hybrid && d.PrimeSelectAvailable, L.T("concluído. Reinicie o computador para usar o driver NVIDIA."));
        return new NvidiaAction(kind, title, kind == NvidiaActionKind.Update ? L.T("Atualizar") : L.T("Instalar"), steps, CommonRisks(d, support), script, ["install", .. pkgs], purge, tested);
    }

    private static NvidiaAction UbuntuRepair(NvidiaDiagnosis d, string branch, bool open, bool tested)
    {
        var kernels = SuspectKernels(d);
        var purge = d.Packages.Where(p => p.Present && (kernels.Any(k => NvidiaScripts.BelongsToKernel(p.Name, k))
                || NvidiaScripts.IsPrebuiltMeta(p.Name)
                || p.Broken && Regex.IsMatch(p.Name, @"^linux-(modules|objects|signatures)-nvidia-")))
            .Select(p => p.Name).Where(n => !NvidiaScripts.IsKernelCore(n, d.RunningKernel)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var pkgs = UbuntuPackages(branch, open, d.RunningKernel);
        var steps = new List<string>();
        var broken = d.BrokenPackages.Select(p => $"{p.Name} ({p.StatusText})").ToList();
        if (broken.Count > 0) steps.Add(L.T("Pacotes pela metade encontrados: ") + string.Join(", ", broken.Take(8)) + (broken.Count > 8 ? L.T($" e mais {broken.Count - 8}") : "") + ".");
        foreach (var k in kernels)
        {
            var h = d.Kernels.First(x => x.Kernel == k);
            steps.Add(L.T($"Remover o kernel {k}, que não está em uso") + (h.Present && !h.Complete ? L.T($" e veio com cabeçalhos incompletos (faltam {string.Join(", ", h.Missing)})") : L.T(" e tem pacotes do driver que falharam")) + L.T(". O computador continua iniciando no kernel atual, ") + d.RunningKernel + ".");
        }
        if (purge.Count > 0) steps.Add(L.T("Pacotes que serão removidos (purge): ") + string.Join(", ", purge) + ".");
        steps.Add(L.T($"Instalar {DkmsPackage(branch, open)} e {DriverPackage(branch, open)} com os cabeçalhos do kernel em uso (linux-headers-{d.RunningKernel}): o DKMS monta o módulo para ele."));
        if (d.Hybrid && d.PrimeSelectAvailable) steps.Add(L.T("Deixar o PRIME em \"on-demand\"."));
        steps.Add(L.T("Conferir no final (dkms status e arquivo do módulo) e pedir para você reiniciar."));
        var risks = CommonRisks(d, d.Primary!.Support);
        if (kernels.Count > 0) risks.Insert(0, L.T("O kernel removido some do menu de boot. Se você o escolhia de propósito, ele volta numa atualização futura da distribuição (com cabeçalhos corrigidos)."));
        var args = new List<string> { "install", "--purge" }; args.AddRange(pkgs); args.AddRange(purge.Select(p => p + "-"));
        string script = NvidiaScripts.Apt(args, NvidiaScripts.GuardRepair(d.RunningKernel), d.RunningKernel, true, d.Hybrid && d.PrimeSelectAvailable, L.T("reparo concluído. Reinicie o computador para usar o driver NVIDIA."));
        return new NvidiaAction(NvidiaActionKind.Repair, L.T($"Reparar a instalação do driver NVIDIA {branch}"), L.T("REPARAR"), steps, risks, script, args, purge, tested);
    }

    private static NvidiaAction UbuntuRevert(NvidiaDiagnosis d, bool tested)
    {
        var purge = d.NvidiaPackages.Select(p => p.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var steps = new List<string>
        {
            L.T("Remover (purge) os pacotes do driver NVIDIA: ") + string.Join(", ", purge.Take(10)) + (purge.Count > 10 ? L.T($" e mais {purge.Count - 10}") : "") + ".",
            L.T("Com isso a regra que bloqueia o nouveau sai junto; no próximo boot a placa volta ao nouveau (driver livre do kernel)."),
            L.T("O kernel, o nvidia-prime e o nvidia-settings não são removidos."),
        };
        var risks = new List<string> { L.T("Sem o driver NVIDIA: jogos e programas que dependem de CUDA/NVENC deixam de usar a placa; o nouveau tem desempenho bem menor e, em placas Maxwell ou mais novas, não controla o clock.") };
        string script = NvidiaScripts.Apt(["purge", .. purge], NvidiaScripts.GuardInstall(d.RunningKernel), d.RunningKernel, false, false, L.T("driver NVIDIA removido. Reinicie o computador para voltar ao nouveau."));
        return new NvidiaAction(NvidiaActionKind.Revert, L.T("Voltar ao nouveau (remover o driver NVIDIA)"), L.T("VOLTAR AO NOUVEAU"), steps, risks, script, ["purge", .. purge], [], tested);
    }

    private static NvidiaAction DebianInstall(NvidiaDiagnosis d)
    {
        string k = d.RunningKernel;
        if (d.Primary!.Generation == NvidiaGeneration.Kepler)
            return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (Debian)"), L.T("Instalar"), [L.T("Debian: o driver legado 470 tem pacotes próprios (nvidia-tesla-470-driver) que mudam entre versões do Debian.")], [], "", null, [], false,
                L.T("Debian com placa Kepler: sem plano automático. Veja https://wiki.debian.org/NvidiaGraphicsDrivers."));
        string[] pkgs = ["nvidia-kernel-dkms", "nvidia-driver", "firmware-misc-nonfree", "linux-headers-" + k];
        var steps = new List<string> { L.T("Debian: instala nvidia-driver (a versão que o Debian publica, não necessariamente a 580) com DKMS e os cabeçalhos do kernel em uso."), L.T("Requer os componentes \"contrib non-free non-free-firmware\" habilitados no sources.list; senão a simulação falha e nada é feito.") };
        var risks = CommonRisks(d, d.Primary.Support); risks.Insert(0, L.T("NÃO TESTADO: plano documentado para Debian, nunca executado de verdade."));
        return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (Debian)"), L.T("Instalar"), steps, risks,
            NvidiaScripts.Apt(["install", .. pkgs], NvidiaScripts.GuardInstall(k), k, true, false, L.T("concluído. Reinicie o computador.")), ["install", .. pkgs], [], false);
    }

    /// <summary>Fedora (RPM Fusion akmod), Arch (pacman + DKMS) and openSUSE (zypper): fixed scripts, all NÃO TESTADO; no simulation step.</summary>
    private static NvidiaAction? OtherFamilyInstall(NvidiaDiagnosis d, NvidiaGpu gpu)
    {
        string k = d.RunningKernel; var gen = gpu.Generation;
        if (!NvidiaScripts.ValidKernel(k)) return null;
        var risks = CommonRisks(d, gpu.Support);
        risks.Insert(0, L.T("NÃO TESTADO: plano documentado, nunca executado de verdade nesta distribuição. Sem simulação prévia: confira o script."));
        bool turingPlus = gen >= NvidiaGeneration.Turing;
        switch (d.Distro.Family)
        {
            case DistroFamily.Fedora:
            {
                string suffix = gen == NvidiaGeneration.Kepler ? "-470xx" : turingPlus ? "" : "-580xx";
                string akmod = "akmod-nvidia" + suffix, cuda = "xorg-x11-drv-nvidia" + suffix + "-cuda";
                string script = string.Join('\n',
                    "set -eu", $"K={NvidiaScripts.Quote(k)}",
                    "echo '== Ilmenyx 1/3: RPM Fusion (repositório do driver NVIDIA)'",
                    "if ! rpm -q rpmfusion-nonfree-release >/dev/null 2>&1; then dnf install -y \"https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm\" \"https://mirrors.rpmfusion.org/nonfree/fedora/rpmfusion-nonfree-release-$(rpm -E %fedora).noarch.rpm\"; fi",
                    $"echo '== Ilmenyx 2/3: dnf install {akmod} {cuda} kernel-devel-'\"$K\"",
                    $"dnf install -y {NvidiaScripts.Quote(akmod)} {NvidiaScripts.Quote(cuda)} \"kernel-devel-$K\"",
                    "echo '== Ilmenyx 3/3: montando o módulo para o kernel em uso (akmods)'",
                    L.T("akmods --force --kernels \"$K\""),
                    "if find \"/lib/modules/$K\" -name 'nvidia.ko*' 2>/dev/null | grep -q .; then echo 'OK: módulo nvidia presente.'; else echo 'ATENÇÃO: módulo nvidia não encontrado.'; exit 4; fi",
                    "echo '== Ilmenyx: concluído. Reinicie o computador.'") + "\n";
                return new NvidiaAction(NvidiaActionKind.Install, L.T($"Instalar o driver NVIDIA ({akmod}, RPM Fusion)"), L.T("Instalar"),
                    [L.T($"Habilitar o RPM Fusion (free e nonfree) se ainda não estiver."), L.T($"Instalar {akmod} e {cuda} com kernel-devel do kernel em uso ({k})."), L.T("Forçar o akmods a montar o módulo agora e conferir o arquivo do módulo."), turingPlus ? L.T("Placas Turing ou mais novas seguem o ramo atual do RPM Fusion (hoje mais novo que o 580).") : L.T("Os nomes dos pacotes legados do RPM Fusion podem mudar: se o dnf não os achar, nada é instalado.")],
                    risks, script, null, [], false);
            }
            case DistroFamily.Arch:
            {
                string? headers = ArchHeaders(k);
                if (!turingPlus || headers is null)
                    return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (Arch)"), L.T("Instalar"), [], risks, "", null, [], false,
                        !turingPlus ? L.T($"Arch e derivadas: placas {NvidiaGenerations.Name(gen)} usam o driver {gpu.Support.Branch} do AUR (nvidia-{gpu.Support.Branch}xx-dkms), que o Ilmenyx não instala. Use um assistente do AUR (yay/paru).") : L.T($"Não reconhecemos o pacote de cabeçalhos do kernel {k}. Instale nvidia-open-dkms e os cabeçalhos do seu kernel pelo pacman."));
                string script = string.Join('\n',
                    "set -eu",
                    $"echo '== Ilmenyx 1/2: pacman -S nvidia-open-dkms nvidia-utils {headers}'",
                    $"pacman -S --needed --noconfirm nvidia-open-dkms nvidia-utils {NvidiaScripts.Quote(headers)}",
                    "echo '== Ilmenyx 2/2: verificação'",
                    $"dkms status -k {NvidiaScripts.Quote(k)} || true",
                    $"if find /usr/lib/modules/{NvidiaScripts.Quote(k)} -name 'nvidia.ko*' 2>/dev/null | grep -q .; then echo 'OK: módulo nvidia presente.'; else echo 'ATENÇÃO: módulo nvidia não encontrado.'; exit 4; fi",
                    "echo '== Ilmenyx: concluído. Reinicie o computador.'") + "\n";
                return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (nvidia-open-dkms)"), L.T("Instalar"),
                    [L.T($"Instalar nvidia-open-dkms, nvidia-utils e {headers} (cabeçalhos do kernel em uso, {k})."), L.T("O DKMS monta o módulo; conferimos o arquivo no final."), L.T("Placas Turing ou mais novas seguem o ramo atual do Arch (hoje mais novo que o 580).")],
                    risks, script, null, [], false);
            }
            case DistroFamily.OpenSuse:
            {
                if (!turingPlus)
                    return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (openSUSE)"), L.T("Instalar"), [], risks, "", null, [], false,
                        L.T("openSUSE com placa anterior à Turing: os pacotes do ramo legado mudam com o repositório da NVIDIA. Use o YaST (Software → Repositórios → NVIDIA)."));
                string script = string.Join('\n',
                    "set -eu",
                    "if ! zypper --non-interactive lr -u | grep -q 'download.nvidia.com'; then echo 'ABORTADO: repositório da NVIDIA não configurado (YaST → Repositórios). Nada foi alterado.'; exit 3; fi",
                    "echo '== Ilmenyx 1/1: zypper install nvidia-open-driver-G06-signed-kmp-default nvidia-video-G06 nvidia-gl-G06 nvidia-compute-utils-G06'",
                    "zypper --non-interactive install --auto-agree-with-licenses nvidia-open-driver-G06-signed-kmp-default nvidia-video-G06 nvidia-gl-G06 nvidia-compute-utils-G06",
                    "echo '== Ilmenyx: concluído. Reinicie o computador.'") + "\n";
                return new NvidiaAction(NvidiaActionKind.Install, L.T("Instalar o driver NVIDIA (G06 open, openSUSE)"), L.T("Instalar"),
                    [L.T("Conferir que o repositório da NVIDIA está configurado (senão, nada é feito)."), L.T("Instalar os módulos \"open\" assinados (G06) e o driver de espaço do usuário.")], risks, script, null, [], false);
            }
            default: return null;
        }
    }

    private static NvidiaAction? OtherFamilyRevert(NvidiaDiagnosis d)
    {
        string? script = d.Distro.Family switch
        {
            DistroFamily.Fedora => "set -eu\necho '== Ilmenyx: removendo o driver NVIDIA (RPM Fusion)'\ndnf remove -y 'akmod-nvidia*' 'kmod-nvidia*' 'xorg-x11-drv-nvidia*'\necho '== Ilmenyx: concluído. Reinicie o computador para voltar ao nouveau.'\n",
            DistroFamily.Arch => "set -eu\necho '== Ilmenyx: removendo o driver NVIDIA (pacman)'\nP=$(pacman -Qq | grep -E '^(nvidia-open-dkms|nvidia-dkms|nvidia|nvidia-open|nvidia-utils|lib32-nvidia-utils|nvidia-[0-9]{3}xx-dkms|nvidia-[0-9]{3}xx-utils)$' || true)\nif [ -n \"$P\" ]; then pacman -Rns --noconfirm $P; fi\necho '== Ilmenyx: concluído. Reinicie o computador para voltar ao nouveau.'\n",
            _ => null,
        };
        return script is null ? null : new NvidiaAction(NvidiaActionKind.Revert, L.T("Voltar ao nouveau (remover o driver NVIDIA)"), L.T("VOLTAR AO NOUVEAU"),
            [L.T("Remover os pacotes do driver NVIDIA; no próximo boot a placa volta ao nouveau.")], [L.T("NÃO TESTADO nesta distribuição."), L.T("Sem o driver NVIDIA, CUDA/NVENC e o desempenho em jogos deixam de existir.")], script, null, [], false);
    }

    /// <summary>Arch kernel flavour → headers package (linux, linux-lts, linux-zen, linux-hardened, CachyOS, Manjaro linuxXY). Null when unknown.</summary>
    public static string? ArchHeaders(string kernel)
    {
        if (kernel.EndsWith("-lts", StringComparison.Ordinal)) return "linux-lts-headers";
        if (kernel.Contains("-zen", StringComparison.Ordinal)) return "linux-zen-headers";
        if (kernel.Contains("-hardened", StringComparison.Ordinal)) return "linux-hardened-headers";
        if (kernel.Contains("cachyos", StringComparison.Ordinal)) return kernel.Contains("-lts", StringComparison.Ordinal) ? "linux-cachyos-lts-headers" : "linux-cachyos-headers";
        if (kernel.Contains("MANJARO", StringComparison.Ordinal) && Regex.Match(kernel, @"^(\d+)\.(\d+)") is { Success: true } m) return $"linux{m.Groups[1].Value}{m.Groups[2].Value}-headers";
        if (Regex.IsMatch(kernel, @"-arch\d+-\d+$")) return "linux-headers";
        return null;
    }

    /// <summary>
    /// Checks the real `apt-get -s` output before anything runs: the plan is refused when apt would install or remove a kernel image
    /// (or kernel modules), touch the running kernel, or remove packages that are not NVIDIA driver packages (repair: plus its listed kernel).
    /// Empty = acceptable.
    /// </summary>
    public static IReadOnlyList<string> CheckSimulation(NvidiaAction action, AptSimulation sim, string runningKernel)
    {
        var problems = new List<string>();
        foreach (var e in sim.Errors) problems.Add(L.T("A simulação do apt falhou: ") + e);
        foreach (var a in sim.Installs)
        {
            if (Regex.IsMatch(a.Package, @"^linux-(image|modules|objects|signatures|main-modules)-"))
                problems.Add(L.T($"A simulação instalaria {a.Package}: isso puxaria outro kernel ou módulos pré-compilados (o que quebrou o caso real). Plano recusado."));
            else if (Regex.IsMatch(a.Package, @"^linux-(.+-)?headers-") && !a.Package.Contains(NvidiaScripts.KernelBase(runningKernel), StringComparison.Ordinal))
                problems.Add(L.T($"A simulação instalaria cabeçalhos de outro kernel ({a.Package}). Plano recusado."));
        }
        foreach (var a in sim.Removals)
        {
            if (NvidiaScripts.IsKernelCore(a.Package, runningKernel))
                problems.Add(L.T($"A simulação removeria {a.Package}, do kernel em uso ({runningKernel}). Plano recusado."));
            else if (a.Package.StartsWith("linux-image-", StringComparison.Ordinal) && !action.AllowedRemovals.Contains(a.Package))
                problems.Add(L.T($"A simulação removeria o kernel {a.Package}. Plano recusado."));
            else if (!NvidiaScripts.IsNvidiaPackage(a.Package) && !action.AllowedRemovals.Contains(a.Package))
                problems.Add(L.T($"A simulação removeria {a.Package}, que não é do driver NVIDIA. Plano recusado."));
        }
        return problems;
    }

    /// <summary>Post-run check on a fresh diagnosis: the module is ready for the running kernel (install/repair/update) or the driver is gone (undo).</summary>
    public static (bool Ok, string Text) Verify(NvidiaActionKind kind, NvidiaDiagnosis after)
    {
        if (kind == NvidiaActionKind.Revert)
            return after.NvidiaPackages.Any(p => !p.Name.StartsWith("linux-", StringComparison.Ordinal))
                ? (false, L.T("Ainda há pacotes do driver NVIDIA instalados: ") + string.Join(", ", after.NvidiaPackages.Select(p => p.Name).Take(6)) + ".")
                : (true, L.T("Driver NVIDIA removido. Reinicie o computador para a placa voltar ao nouveau."));
        if (after.BrokenPackages.Any()) return (false, L.T("Ainda há pacotes pela metade: ") + string.Join(", ", after.BrokenPackages.Select(p => p.Name + " (" + p.StatusText + ")").Take(6)) + L.T(". Veja o log."));
        if (!after.ModuleReadyForRunningKernel) return (false, L.T($"O módulo NVIDIA não foi encontrado para o kernel {after.RunningKernel}. Veja o log (DKMS)."));
        string dkms = after.Dkms.FirstOrDefault(x => x.Kernel == after.RunningKernel && x.Module.StartsWith("nvidia", StringComparison.Ordinal)) is { } e ? $" (dkms: {e.Module}/{e.Version} {e.State})" : "";
        return (true, L.T($"Módulo NVIDIA pronto para o kernel {after.RunningKernel}{dkms}. Reinicie o computador para ativar o driver."));
    }

    public static IReadOnlyList<string> Findings(NvidiaDiagnosis d, NvidiaDriverState state)
    {
        var f = new List<string>();
        foreach (var g in d.Gpus)
            f.Add(L.T($"GPU NVIDIA: {g.Name} [10de:{g.DeviceId}] · {NvidiaGenerations.Name(g.Generation)}{(g.Chip.Length > 0 ? " " + g.Chip : "")} · driver em uso: {(g.KernelDriver.Length > 0 ? g.KernelDriver : L.T("nenhum"))} · ramo indicado: {g.Support.Branch ?? "nenhum"}"));
        if (d.OtherGpus.Count > 0) f.Add(L.T("Outra GPU: ") + string.Join(", ", d.OtherGpus) + (d.Gpus.Count > 0 ? L.T(" (notebook/PC híbrido: PRIME)") : ""));
        f.Add(L.T($"Sistema: {d.Distro.Name} ({FamilyText(d.Distro.Family)}) · kernel em uso {d.RunningKernel} ({d.Architecture})"));
        if (d.RunningHeaders is { } h) f.Add(h.Complete ? L.T($"Cabeçalhos do kernel em uso: completos") : h.Present ? L.T($"Cabeçalhos do kernel em uso: INCOMPLETOS (faltam {string.Join(", ", h.Missing)})") : L.T("Cabeçalhos do kernel em uso: ausentes (serão instalados junto)"));
        foreach (var k in d.Kernels.Where(k => k.Kernel != d.RunningKernel))
            f.Add(L.T($"Outro kernel instalado: {k.Kernel} · cabeçalhos {(k.Complete ? L.T("completos") : k.Present ? L.T("INCOMPLETOS (faltam ") + string.Join(", ", k.Missing) + ")" : L.T("ausentes"))}"));
        foreach (var p in d.BrokenPackages.Take(12)) f.Add(L.T($"Pacote pela metade: {p.Name} {p.Version} · {p.Status.Trim()} ({p.StatusText})"));
        if (d.InstalledDriver is { } i) f.Add(L.T($"Pacote do driver: nvidia-driver-{i.Branch}{(i.Open ? "-open" : "")} {i.Version}") + (d.CandidateVersion is { } c ? c != i.Version ? L.T($" · disponível: {c}") : L.T(" · em dia (é o candidato do gerenciador de pacotes para este ramo)") : ""));
        foreach (var e in d.Dkms.Where(e => e.Module.StartsWith("nvidia", StringComparison.Ordinal))) f.Add($"DKMS: {e.Module}/{e.Version} · {e.Kernel} · {e.State}");
        if (d.LoadedVersion is { } lv) f.Add(L.T($"Módulo carregado: nvidia {lv}"));
        if (d.SmiVersion is { } sv) f.Add(L.T($"nvidia-smi: responde (driver {sv})")); else if (d.SmiInstalled) f.Add(L.T("nvidia-smi: instalado, mas não respondeu"));
        f.Add("Secure Boot: " + SecureBootText(d.SecureBoot));
        if (d.PrimeMode is { } pm) f.Add(L.T("PRIME (prime-select): ") + pm);
        f.AddRange(d.Notes);
        return f;
    }

    public static string SecureBootText(SecureBootState s) => s switch { SecureBootState.Enabled => "ligado", SecureBootState.Disabled => "desligado", SecureBootState.NotUefi => L.T("não se aplica (BIOS legado)"), _ => "desconhecido" };
    public static string FamilyText(DistroFamily f) => f switch
    {
        DistroFamily.Ubuntu => "base Ubuntu, apt", DistroFamily.Debian => "Debian, apt", DistroFamily.Fedora => "Fedora, dnf + RPM Fusion", DistroFamily.Arch => "Arch, pacman",
        DistroFamily.OpenSuse => L.T("openSUSE, zypper"), _ => L.T("fora da lista: só orientação"),
    };
}
