// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// "Driver NVIDIA" tab, pure part: GPU generation table (which driver branch supports each generation), parsers of os-release,
// dpkg-query, apt-get -s, dkms status, mokutil and apt-cache policy, and the diagnosis record filled by the Linux probe.
// Nothing here runs a process or touches the system.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public enum NvidiaGeneration { Unknown, Tesla, Fermi, Kepler, Maxwell, Pascal, Volta, Turing, Ampere, Hopper, Ada, Blackwell }

/// <summary>Whether the NVIDIA "open" kernel modules work on a generation: no (before Turing), optional (Turing to Ada), required (Blackwell).</summary>
public enum NvidiaOpenModules { Unsupported, Optional, Required }

/// <param name="Branch">Driver branch to install (3 digits) or null when no supported NVIDIA driver exists for this generation.</param>
/// <param name="Legacy">The branch no longer receives new features (Kepler: 470, end of life since 2024).</param>
public sealed record NvidiaBranchSupport(NvidiaGeneration Generation, string? Branch, bool Legacy, NvidiaOpenModules Open, string Note);

public static class NvidiaGenerations
{
    /// <summary>PCI device id ranges (inclusive) of NVIDIA GPU chips, from the public PCI id lists. Ids outside every range are Unknown.</summary>
    private static readonly (int From, int To, NvidiaGeneration Generation, string Chip)[] Ranges =
    [
        (0x0040, 0x06BF, NvidiaGeneration.Tesla, "NV4x/G8x/G9x"), (0x06E0, 0x06FF, NvidiaGeneration.Tesla, "G98"), (0x07E0, 0x087F, NvidiaGeneration.Tesla, "MCP7x"),
        (0x0A20, 0x0AFF, NvidiaGeneration.Tesla, "GT21x"), (0x0CA0, 0x0CBF, NvidiaGeneration.Tesla, "GT215"), (0x10C0, 0x10DF, NvidiaGeneration.Tesla, "GT218"),
        (0x06C0, 0x06DF, NvidiaGeneration.Fermi, "GF100"), (0x0DC0, 0x0DFF, NvidiaGeneration.Fermi, "GF106/GF108"), (0x0E20, 0x0E3F, NvidiaGeneration.Fermi, "GF104"),
        (0x1040, 0x107F, NvidiaGeneration.Fermi, "GF119"), (0x1080, 0x109F, NvidiaGeneration.Fermi, "GF110"), (0x1140, 0x117F, NvidiaGeneration.Fermi, "GF117"),
        (0x1200, 0x121F, NvidiaGeneration.Fermi, "GF114"), (0x1240, 0x125F, NvidiaGeneration.Fermi, "GF116"),
        (0x0FC0, 0x0FFF, NvidiaGeneration.Kepler, "GK107"), (0x1000, 0x103F, NvidiaGeneration.Kepler, "GK110/GK210"), (0x1180, 0x11BF, NvidiaGeneration.Kepler, "GK104"),
        (0x11C0, 0x11FF, NvidiaGeneration.Kepler, "GK106"), (0x1280, 0x12BF, NvidiaGeneration.Kepler, "GK208"),
        (0x1340, 0x137F, NvidiaGeneration.Maxwell, "GM108"), (0x1380, 0x13BF, NvidiaGeneration.Maxwell, "GM107"), (0x13C0, 0x13FF, NvidiaGeneration.Maxwell, "GM204"),
        (0x1400, 0x143F, NvidiaGeneration.Maxwell, "GM206"), (0x17C0, 0x17FF, NvidiaGeneration.Maxwell, "GM200"),
        (0x15F0, 0x15FF, NvidiaGeneration.Pascal, "GP100"), (0x1B00, 0x1B7F, NvidiaGeneration.Pascal, "GP102"), (0x1B80, 0x1BFF, NvidiaGeneration.Pascal, "GP104"),
        (0x1C00, 0x1C7F, NvidiaGeneration.Pascal, "GP106"), (0x1C80, 0x1CFF, NvidiaGeneration.Pascal, "GP107"), (0x1D00, 0x1D7F, NvidiaGeneration.Pascal, "GP108"),
        (0x1D80, 0x1DFF, NvidiaGeneration.Volta, "GV100"),
        (0x1E00, 0x1E7F, NvidiaGeneration.Turing, "TU102"), (0x1E80, 0x1EFF, NvidiaGeneration.Turing, "TU104"), (0x1F00, 0x1F7F, NvidiaGeneration.Turing, "TU106"),
        (0x1F80, 0x1FFF, NvidiaGeneration.Turing, "TU117"), (0x2180, 0x21FF, NvidiaGeneration.Turing, "TU116"),
        (0x20B0, 0x20FF, NvidiaGeneration.Ampere, "GA100"), (0x2200, 0x22FF, NvidiaGeneration.Ampere, "GA102"), (0x2400, 0x247F, NvidiaGeneration.Ampere, "GA103/GA104"),
        (0x2480, 0x24FF, NvidiaGeneration.Ampere, "GA104"), (0x2500, 0x255F, NvidiaGeneration.Ampere, "GA106"), (0x2560, 0x25FF, NvidiaGeneration.Ampere, "GA107"),
        (0x2300, 0x23FF, NvidiaGeneration.Hopper, "GH100"),
        (0x2600, 0x26FF, NvidiaGeneration.Ada, "AD102"), (0x2700, 0x277F, NvidiaGeneration.Ada, "AD103"), (0x2780, 0x27FF, NvidiaGeneration.Ada, "AD104"),
        (0x2800, 0x287F, NvidiaGeneration.Ada, "AD106"), (0x2880, 0x28FF, NvidiaGeneration.Ada, "AD107"),
        (0x2900, 0x2AFF, NvidiaGeneration.Blackwell, "GB100/GB102"), (0x2B00, 0x2BFF, NvidiaGeneration.Blackwell, "GB202"), (0x2C00, 0x2CFF, NvidiaGeneration.Blackwell, "GB203"),
        (0x2D00, 0x2FFF, NvidiaGeneration.Blackwell, "GB20x"),
    ];

    /// <summary>Current NVIDIA production branch (download.nvidia.com latest.txt = 595.112.07 on 2026-11-07; Ubuntu 24.04 ships nvidia-driver-595).</summary>
    public const string CurrentBranch = "595";
    /// <summary>The 590+ series dropped Maxwell, Pascal and Volta: 580 is the last branch that supports them.</summary>
    public const string LegacyMaxwellBranch = "580";
    private const string LegacyNote = "Ramo 580 (legado): o último da NVIDIA que suporta Maxwell, Pascal e Volta — a série 590+ removeu essas placas. Módulos proprietários.";
    private const string CurrentNote = "Ramo atual 595 com os módulos \"open\" do kernel, recomendados pela NVIDIA a partir da Turing (RTX 20/GTX 16).";

    /// <summary>The branch table shown in the tab and in docs/driver-nvidia.md. Only branches listed here can reach a script.</summary>
    public static readonly IReadOnlyList<NvidiaBranchSupport> Table =
    [
        new(NvidiaGeneration.Tesla, null, false, NvidiaOpenModules.Unsupported, "Sem driver NVIDIA suportado (o último, 340, acabou em 2019 e não compila nos kernels atuais). Use o nouveau, que já vem no kernel."),
        new(NvidiaGeneration.Fermi, null, false, NvidiaOpenModules.Unsupported, "Sem driver NVIDIA suportado (o último, 390, acabou em 2022 e não compila nos kernels atuais). Use o nouveau, que já vem no kernel."),
        new(NvidiaGeneration.Kepler, "470", true, NvidiaOpenModules.Unsupported, "Driver legado 470: fim do suporte da NVIDIA em 2024 (só correções da distribuição). Pode deixar de compilar em kernels novos."),
        new(NvidiaGeneration.Maxwell, "580", true, NvidiaOpenModules.Unsupported, LegacyNote),
        new(NvidiaGeneration.Pascal, "580", true, NvidiaOpenModules.Unsupported, LegacyNote),
        new(NvidiaGeneration.Volta, "580", true, NvidiaOpenModules.Unsupported, LegacyNote),
        new(NvidiaGeneration.Turing, CurrentBranch, false, NvidiaOpenModules.Optional, CurrentNote),
        new(NvidiaGeneration.Ampere, CurrentBranch, false, NvidiaOpenModules.Optional, CurrentNote),
        new(NvidiaGeneration.Hopper, CurrentBranch, false, NvidiaOpenModules.Optional, CurrentNote + " (GPU de data center)"),
        new(NvidiaGeneration.Ada, CurrentBranch, false, NvidiaOpenModules.Optional, CurrentNote),
        new(NvidiaGeneration.Blackwell, CurrentBranch, false, NvidiaOpenModules.Required, $"Ramo atual {CurrentBranch} com os módulos \"open\" do kernel: a Blackwell só funciona com eles."),
        new(NvidiaGeneration.Unknown, null, false, NvidiaOpenModules.Unsupported, "Modelo fora da tabela do Ilmenyx: não instalamos nada automaticamente. Consulte o gerenciador de drivers da sua distribuição."),
    ];

    public static IReadOnlySet<string> Branches { get; } = Table.Where(t => t.Branch is not null).Select(t => t.Branch!).ToHashSet(StringComparer.Ordinal);

    public static (NvidiaGeneration Generation, string Chip) Classify(string deviceId)
    {
        string hex = deviceId.Trim().ToLowerInvariant();
        if (hex.StartsWith("0x", StringComparison.Ordinal)) hex = hex[2..];
        if (hex.Length is 0 or > 4 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id)) return (NvidiaGeneration.Unknown, "");
        foreach (var (from, to, generation, chip) in Ranges) if (id >= from && id <= to) return (generation, chip);
        return (NvidiaGeneration.Unknown, "");
    }

    public static NvidiaBranchSupport Support(NvidiaGeneration generation) => Table.First(t => t.Generation == generation);

    /// <summary>pci.ids "GM108M [GeForce 940MX]" → "GeForce 940MX" (the marketing name in brackets); otherwise the text as is.</summary>
    public static string MarketingName(string pciName)
    {
        int open = pciName.LastIndexOf('['), close = pciName.LastIndexOf(']');
        return open >= 0 && close > open + 1 ? pciName[(open + 1)..close].Trim() : pciName.Trim();
    }

    /// <summary>
    /// Newest version of one branch in a listing of https://download.nvidia.com/XFree86/Linux-x86_64/ (directory names "580.105.08/").
    /// Used so a Maxwell card is compared with the newest 580.x, never with the global latest (595+ does not support it).
    /// </summary>
    public static string? NewestInBranch(string listing, string branch)
    {
        if (!NvidiaScripts.ValidBranch(branch)) return null;
        Version? best = null; string? text = null;
        foreach (Match m in Regex.Matches(listing, @"(?<![\d.])(" + branch + @"\.\d{1,3}(?:\.\d{1,3})?)/"))
            if (Version.TryParse(m.Groups[1].Value, out var v) && (best is null || v > best)) { best = v; text = m.Groups[1].Value; }
        return text;
    }

    public static string Name(NvidiaGeneration g) => g switch { NvidiaGeneration.Unknown => "geração desconhecida", NvidiaGeneration.Tesla => "Tesla ou anterior", _ => g.ToString() };
}

public enum DistroFamily { Ubuntu, Debian, Fedora, Arch, OpenSuse, Other }

public sealed record DistroInfo(string Id, string IdLike, string Name, string VersionId, DistroFamily Family)
{
    public bool UsesApt => Family is DistroFamily.Ubuntu or DistroFamily.Debian;

    public static DistroInfo Parse(string osRelease)
    {
        string F(string key) => osRelease.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(key + "=", StringComparison.Ordinal))
            .Select(l => l[(key.Length + 1)..].Trim().Trim('"', '\'')).FirstOrDefault() ?? "";
        string id = F("ID").ToLowerInvariant(), like = F("ID_LIKE").ToLowerInvariant();
        string name = F("PRETTY_NAME") is { Length: > 0 } p ? p : F("NAME");
        var tokens = new[] { id }.Concat(like.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        // The distro's own ID decides first (Mint/Pop are Ubuntu-based, Nobara is Fedora-based, CachyOS/EndeavourOS/Manjaro are Arch-based).
        DistroFamily Of(string t) => t switch
        {
            "ubuntu" or "linuxmint" or "pop" or "elementary" or "zorin" or "neon" or "kubuntu" or "xubuntu" => DistroFamily.Ubuntu,
            "debian" or "raspbian" => DistroFamily.Debian,
            "fedora" or "nobara" => DistroFamily.Fedora,
            "arch" or "manjaro" or "cachyos" or "endeavouros" or "garuda" => DistroFamily.Arch,
            _ when t.StartsWith("opensuse", StringComparison.Ordinal) || t is "suse" or "sles" => DistroFamily.OpenSuse,
            _ => DistroFamily.Other,
        };
        var family = tokens.Select(Of).FirstOrDefault(f => f != DistroFamily.Other, DistroFamily.Other);
        // Ubuntu-likes list "ubuntu debian": the Ubuntu packaging wins over plain Debian.
        if (family == DistroFamily.Debian && tokens.Contains("ubuntu")) family = DistroFamily.Ubuntu;
        return new DistroInfo(id, like, name.Length > 0 ? name : id, F("VERSION_ID"), family);
    }
}

/// <summary>One dpkg package with its 2–3 letter status (desired, current, error): "ii", "iF", "iU", "iHR"…</summary>
public sealed record DpkgPackage(string Status, string Name, string Version)
{
    private char Current => Status.Length >= 2 ? Status[1] : 'n';
    public bool Present => Current != 'n';
    public bool Installed => Current == 'i';
    /// <summary>Half-installed, unpacked but not configured, half-configured, waiting for triggers, or flagged reinstall-required.</summary>
    public bool Broken => Current is 'U' or 'F' or 'H' or 'W' or 't' || (Status.Length >= 3 && Status[2] == 'R');

    public string StatusText => Current switch
    {
        'i' => "instalado", 'c' => "só configuração", 'U' => "desempacotado, não configurado", 'F' => "configuração falhou", 'H' => "instalação pela metade",
        'W' => "aguardando gatilhos", 't' => "gatilhos pendentes", _ => "não instalado",
    } + (Status.Length >= 3 && Status[2] == 'R' ? " (reinstalação exigida)" : "");

    /// <summary>
    /// Parses `dpkg-query -W -f='${db:Status-Abbrev}|${binary:Package}|${Version}\n'` (preferred) or `dpkg -l`. The architecture suffix
    /// (":amd64", ":i386") is dropped and duplicates merged; packages that were never installed ("un") are skipped.
    /// </summary>
    public static IReadOnlyList<DpkgPackage> Parse(string text)
    {
        var list = new List<DpkgPackage>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string status, name, version;
            if (line.Count(c => c == '|') == 2)
            {
                var p = line.Split('|'); status = p[0].Trim(); name = p[1].Trim(); version = p[2].Trim();
            }
            else
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3 || p[0].Length is < 2 or > 3 || !char.IsAsciiLetterLower(p[0][0])) continue;
                status = p[0]; name = p[1]; version = p[2];
            }
            int colon = name.IndexOf(':'); if (colon > 0) name = name[..colon];
            if (status.Length < 2 || !NvidiaScripts.ValidPackage(name)) continue;
            var pkg = new DpkgPackage(status, name, version);
            if (!pkg.Present || !seen.Add(name)) continue;
            list.Add(pkg);
        }
        return list;
    }
}

public sealed record AptAction(string Kind, string Package, string Version);

/// <summary>Result of `apt-get -s …` (a real dry run that needs no root): what apt would install (Inst), remove (Remv/Purg) and configure.</summary>
public sealed record AptSimulation(IReadOnlyList<AptAction> Actions, IReadOnlyList<string> Errors, string Summary, int ExitCode, string Output)
{
    public IEnumerable<AptAction> Installs => Actions.Where(a => a.Kind == "Inst");
    public IEnumerable<AptAction> Removals => Actions.Where(a => a.Kind is "Remv" or "Purg");

    private static readonly Regex Line = new(@"^(Inst|Remv|Purg|Conf)\s+([a-z0-9][a-z0-9+.\-]*)(?::[a-z0-9]+)?\s*(?:\[([^\]]*)\])?\s*(?:\(([^ )]+))?", RegexOptions.CultureInvariant);

    public static AptSimulation Parse(string output, int exitCode)
    {
        var actions = new List<AptAction>(); var errors = new List<string>(); string summary = "";
        foreach (var raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (Line.Match(line) is { Success: true } m)
                actions.Add(new AptAction(m.Groups[1].Value, m.Groups[2].Value, m.Groups[4].Success ? m.Groups[4].Value : m.Groups[3].Value));
            else if (line.StartsWith("E:", StringComparison.Ordinal)) errors.Add(line[2..].Trim());
            else if (line.Contains(" upgraded, ", StringComparison.Ordinal) && line.Contains("newly installed", StringComparison.Ordinal)) summary = line.Trim();
        }
        if (exitCode != 0 && errors.Count == 0) errors.Add("apt-get -s terminou com código " + exitCode + ".");
        return new AptSimulation(actions, errors, summary, exitCode, output);
    }
}

/// <summary>One line of `dkms status`: "nvidia/580.178.04, 6.14.0-37-generic, x86_64: installed" (DKMS 3) or "nvidia, 580…, kernel, arch: installed".</summary>
public sealed record DkmsEntry(string Module, string Version, string Kernel, string State)
{
    public static IReadOnlyList<DkmsEntry> Parse(string text)
    {
        var list = new List<DkmsEntry>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim(); int colon = line.LastIndexOf(':');
            if (colon < 0) continue;
            string state = line[(colon + 1)..].Trim(); var p = line[..colon].Split(',', StringSplitOptions.TrimEntries);
            if (p.Length < 2) continue;
            string module, version; string kernel;
            if (p[0].Contains('/')) { var mv = p[0].Split('/', 2); module = mv[0]; version = mv[1]; kernel = p[1]; }
            else if (p.Length >= 3) { module = p[0]; version = p[1]; kernel = p[2]; }
            else continue;
            int warn = state.IndexOf(' '); if (warn > 0) state = state[..warn]; // "installed (WARNING! Diff between built and installed module!)"
            list.Add(new DkmsEntry(module, version, kernel, state));
        }
        return list;
    }
}

public enum SecureBootState { Unknown, Disabled, Enabled, NotUefi }

public static class NvidiaProbeText
{
    /// <summary>`mokutil --sb-state`: "SecureBoot enabled" / "SecureBoot disabled" / "This system doesn't support Secure Boot" (legacy BIOS).</summary>
    public static SecureBootState SecureBoot(string mokutil)
    {
        string t = mokutil.ToLowerInvariant();
        if (t.Contains("secureboot enabled")) return SecureBootState.Enabled;
        if (t.Contains("secureboot disabled")) return SecureBootState.Disabled;
        if (t.Contains("doesn't support secure boot") || t.Contains("efi variables are not supported")) return SecureBootState.NotUefi;
        return SecureBootState.Unknown;
    }

    /// <summary>`prime-select query` prints one of nvidia, intel, on-demand (anything else is not a PRIME mode).</summary>
    public static string? Prime(string output) => output.Trim() is "nvidia" or "intel" or "on-demand" or "offload" ? output.Trim() : null;

    /// <summary>`apt-cache policy PKG`: (Installed, Candidate); "(none)" becomes null.</summary>
    public static (string? Installed, string? Candidate) Policy(string output)
    {
        string? F(string key) => output.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(key + ":", StringComparison.Ordinal))
            .Select(l => l[(key.Length + 1)..].Trim()).Where(v => v.Length > 0 && v != "(none)").FirstOrDefault();
        return (F("Installed"), F("Candidate"));
    }

    /// <summary>`nvidia-smi --query-gpu=driver_version --format=csv,noheader`: first line when it is a version.</summary>
    public static string? SmiVersion(string output) =>
        output.Split('\n').Select(l => l.Trim()).FirstOrDefault() is { } v && Regex.IsMatch(v, @"^\d{3}\.\d{1,3}(\.\d{1,3})?$") ? v : null;
}

/// <summary>Kernel headers of one kernel: present (the build directory exists) and complete (the files a module build needs).</summary>
public sealed record KernelHeaders(string Kernel, bool Present, IReadOnlyList<string> Missing)
{
    public static readonly IReadOnlyList<string> RequiredFiles = ["Makefile", "scripts/module.lds", "tools/objtool/objtool"];
    public bool Complete => Present && Missing.Count == 0;
}

public sealed record NvidiaGpu(string Slot, string DeviceId, string Name, NvidiaGeneration Generation, string Chip, string KernelDriver)
{
    public NvidiaBranchSupport Support => NvidiaGenerations.Support(Generation);
}

/// <summary>Everything the plan needs, read without root. Null/empty = not available (never guessed).</summary>
public sealed record NvidiaDiagnosis
{
    public IReadOnlyList<NvidiaGpu> Gpus { get; init; } = [];
    /// <summary>Other display controllers (Intel/AMD integrated GPU): a hybrid notebook when there is also an NVIDIA GPU.</summary>
    public IReadOnlyList<string> OtherGpus { get; init; } = [];
    public DistroInfo Distro { get; init; } = DistroInfo.Parse("");
    public string RunningKernel { get; init; } = "";
    public string Architecture { get; init; } = "x86_64";
    public IReadOnlyList<KernelHeaders> Kernels { get; init; } = [];
    public SecureBootState SecureBoot { get; init; }
    public string? PrimeMode { get; init; }
    public bool PrimeSelectAvailable { get; init; }
    /// <summary>/sys/module/nvidia/version: the proprietary/open NVIDIA module is loaded right now.</summary>
    public string? LoadedVersion { get; init; }
    /// <summary>nvidia-smi answered with this driver version (null: missing or failed).</summary>
    public string? SmiVersion { get; init; }
    public bool SmiInstalled { get; init; }
    public IReadOnlyList<DpkgPackage> Packages { get; init; } = [];
    public IReadOnlyList<DkmsEntry> Dkms { get; init; } = [];
    /// <summary>Kernel module files found under /lib/modules/&lt;running&gt; (DKMS "updates/dkms" or a prebuilt package).</summary>
    public IReadOnlyList<string> ModuleFiles { get; init; } = [];
    /// <summary>apt-cache policy of the recommended driver metapackage: (installed, candidate).</summary>
    public string? CandidateVersion { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Hybrid => Gpus.Count > 0 && OtherGpus.Count > 0;
    public KernelHeaders? RunningHeaders => Kernels.FirstOrDefault(k => k.Kernel == RunningKernel);
    public NvidiaGpu? Primary => Gpus.OrderByDescending(g => g.Generation).FirstOrDefault();
    public string KernelDriver => Primary?.KernelDriver is { Length: > 0 } d ? d : "nenhum";
    public IEnumerable<DpkgPackage> NvidiaPackages => Packages.Where(p => NvidiaScripts.IsNvidiaPackage(p.Name));
    public IEnumerable<DpkgPackage> BrokenPackages => Packages.Where(p => p.Broken);

    /// <summary>Installed driver metapackage "nvidia-driver-NNN[-open|-server…]" → (branch, open).</summary>
    public (string Branch, bool Open, string Version)? InstalledDriver =>
        Packages.Where(p => p.Present).Select(p => (p, m: Regex.Match(p.Name, @"^nvidia-driver-(\d{3})(-open)?$"))).Where(x => x.m.Success)
            .Select(x => ((string, bool, string)?)(x.m.Groups[1].Value, x.m.Groups[2].Success, x.p.Version)).FirstOrDefault();

    /// <summary>A module exists for the running kernel (DKMS installed for it, or a module file is on disk).</summary>
    public bool ModuleReadyForRunningKernel =>
        Dkms.Any(d => d.Module.StartsWith("nvidia", StringComparison.Ordinal) && d.Kernel == RunningKernel && d.State == "installed") || ModuleFiles.Count > 0;
}
