// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// ZRAM (compressed swap in RAM) through the distribution's official way: zram-generator (/etc/systemd/zram-generator.conf) on Fedora,
// Arch and openSUSE, zram-tools (/etc/default/zramswap) on Debian/Ubuntu/Mint. Read-only detection, then a plan in plain language and
// FIXED root scripts run through polkit only after the user's click and confirmation (AGENTS.md, "Correções de sensores com um clique").
// Scripts contain only constants and the validated size; our files carry a marker and the undo touches nothing else. Independent code.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public sealed record SwapEntry(string Name, string Type, long SizeKiB, long UsedKiB, int Priority);

public sealed record ZramDevice(string Name, long DiskSizeBytes, string Algorithm);

public enum ZramTool { Generator, ZramTools }

public enum ZramState { Unsupported, Unknown, ActiveOurs, Active, ConfiguredInactive, ForeignConfig, NotNeeded, Recommended }

public enum ZramActionKind { Apply, Undo, UndoAndRemovePackage }

/// <summary>Read-only facts. Config texts are null when the file does not exist.</summary>
public sealed record ZramFacts
{
    public DistroInfo Distro { get; init; } = DistroInfo.Parse("");
    public long? MemTotalBytes { get; init; }
    public IReadOnlyList<SwapEntry> Swaps { get; init; } = [];
    public IReadOnlyList<ZramDevice> Devices { get; init; } = [];
    public string? EtcGeneratorConf { get; init; }
    public string? VendorGeneratorConf { get; init; }
    public IReadOnlyList<string> GeneratorDropIns { get; init; } = [];
    public string? ZramswapDefaults { get; init; }
    public bool GeneratorInstalled { get; init; }
    public bool ZramToolsInstalled { get; init; }
    /// <summary>Ubuntu's older "zram-config" package (its own service): another ZRAM manager we never touch.</summary>
    public bool ZramConfigInstalled { get; init; }
    /// <summary>/var/lib/ilmenyx/zram.state: "package=&lt;name&gt;" when the Ilmenyx installed the package.</summary>
    public string? State { get; init; }
}

/// <param name="Tested">Exercised for real on a machine; false = NÃO TESTADO (shown on the card).</param>
public sealed record ZramAction(ZramActionKind Kind, string Title, string Button, IReadOnlyList<string> Steps, IReadOnlyList<string> Risks, string Script, bool Tested = false);

public sealed record ZramPlan(ZramState State, string Headline, string Summary, IReadOnlyList<string> Findings, IReadOnlyList<ZramAction> Actions, ZramTool? Tool, long? SizeMiB);

public static class ZramPlanner
{
    public const string Marker = "# Ilmenyx-ZRAM";
    public const string GeneratorConf = "/etc/systemd/zram-generator.conf";
    public const string ZramswapConf = "/etc/default/zramswap";
    public const string ZramswapBackup = "/etc/default/zramswap.ilmenyx-bak";
    public const string StateFile = "/var/lib/ilmenyx/zram.state";
    /// <summary>Recommended only up to 16 GiB of RAM (MemTotal of a "16 GB" PC is a bit less than 16 GiB).</summary>
    public const long MaxRamBytes = 16L << 30;
    public const long MaxSizeMiB = 8192;

    public static IReadOnlyList<SwapEntry> ParseSwaps(string text)
    {
        var list = new List<SwapEntry>();
        foreach (var line in text.Split('\n').Skip(1))
        {
            var p = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5) continue;
            long.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size);
            long.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out long used);
            int.TryParse(p[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int prio);
            list.Add(new(p[0].Replace("\\040", " "), p[1], size, used, prio));
        }
        return list;
    }

    /// <summary>MemTotal from /proc/meminfo in bytes.</summary>
    public static long? ParseMemTotal(string meminfo)
    {
        var m = Regex.Match(meminfo, @"^MemTotal:\s+(\d+)\s*kB", RegexOptions.Multiline);
        return m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long kb) ? kb * 1024 : null;
    }

    /// <summary>Active algorithm from /sys/block/zramN/comp_algorithm ("lzo lzo-rle [zstd]").</summary>
    public static string ActiveAlgorithm(string text) => Regex.Match(text, @"\[([a-z0-9-]+)\]") is { Success: true } m ? m.Groups[1].Value : text.Trim();

    public static ZramTool? ToolFor(DistroFamily family) => family switch
    {
        DistroFamily.Fedora or DistroFamily.Arch or DistroFamily.OpenSuse => ZramTool.Generator,
        DistroFamily.Ubuntu or DistroFamily.Debian => ZramTool.ZramTools,
        _ => null,
    };

    public static string PackageFor(ZramTool tool) => tool == ZramTool.Generator ? "zram-generator" : "zram-tools";

    /// <summary>min(RAM/2, 8 GiB) in MiB.</summary>
    public static long SizeMiB(long memTotalBytes) => Math.Clamp(memTotalBytes / 2 / 1048576, 256, MaxSizeMiB);

    public static bool Ours(string? text) => text is not null && text.Split('\n').Any(l => l.Trim() == Marker || l.StartsWith(Marker + ":", StringComparison.Ordinal));

    public static bool InstalledByUs(ZramFacts f, ZramTool tool) => (f.State ?? "").Split('\n').Any(l => l.Trim() == "package=" + PackageFor(tool));

    public static string GiB(long bytes) => (bytes / 1073741824.0).ToString("0.#", CultureInfo.InvariantCulture) + " GiB";

    // ---- fixed scripts ----

    private static (string Installed, string Install, string Remove) PackageCommands(DistroFamily family, string pkg) => family switch
    {
        DistroFamily.Ubuntu or DistroFamily.Debian => ($"dpkg-query -W -f='${{Status}}' {pkg} 2>/dev/null | grep -q 'install ok installed'",
            $"apt-get -y install {pkg} || {{ apt-get update && apt-get -y install {pkg}; }}", $"apt-get -y purge {pkg}"),
        DistroFamily.Fedora => ($"rpm -q {pkg} >/dev/null 2>&1", $"dnf install -y {pkg}", $"dnf remove -y {pkg}"),
        DistroFamily.Arch => ($"pacman -Qi {pkg} >/dev/null 2>&1", $"pacman -S --needed --noconfirm {pkg}", $"pacman -R --noconfirm {pkg}"),
        DistroFamily.OpenSuse => ($"rpm -q {pkg} >/dev/null 2>&1", $"zypper --non-interactive install {pkg}", $"zypper --non-interactive remove {pkg}"),
        _ => throw new ArgumentException(L.T("Distribuição sem caminho oficial de ZRAM.")),
    };

    private static string Header(string title) => string.Join('\n', "set -eu", L.T("export DEBIAN_FRONTEND=noninteractive"), "export LC_ALL=C", $"echo '== Ilmenyx: {title}'") + "\n";

    public static string ApplyScript(DistroFamily family, long sizeMiB)
    {
        var tool = ToolFor(family) ?? throw new ArgumentException(L.T("Distribuição sem caminho oficial de ZRAM."));
        if (sizeMiB is < 256 or > MaxSizeMiB) throw new ArgumentOutOfRangeException(nameof(sizeMiB));
        string pkg = PackageFor(tool);
        var (installed, install, _) = PackageCommands(family, pkg);
        string size = sizeMiB.ToString(CultureInfo.InvariantCulture);
        var s = new List<string>
        {
            "echo '== Ilmenyx 1/4: conferência (nada muda se o ZRAM já estiver ativo ou houver outra configuração)'",
            "if grep -q '^/dev/zram' /proc/swaps; then echo 'ZRAM já está ativo; nada foi alterado.'; exit 3; fi",
        };
        if (tool == ZramTool.Generator)
            s.Add($"if [ -e {GeneratorConf} ] && ! grep -q '^{Marker}' {GeneratorConf}; then echo 'ABORTADO: {GeneratorConf} já existe e não é do Ilmenyx; nada foi alterado.'; exit 3; fi");
        else
            s.Add("if dpkg-query -W -f='${Status}' zram-config 2>/dev/null | grep -q 'install ok installed'; then echo 'ABORTADO: o pacote zram-config (outro gerenciador de ZRAM) está instalado; nada foi alterado.'; exit 3; fi");
        s.Add(L.T("mkdir -p /var/lib/ilmenyx"));
        s.Add($"echo '== Ilmenyx 2/4: pacote {pkg} (oficial da distribuição)'");
        s.Add($"if {installed}; then echo 'Pacote {pkg} já instalado.'; else {install}; grep -qx 'package={pkg}' {StateFile} 2>/dev/null || echo 'package={pkg}' >> {StateFile}; fi");
        s.Add("echo '== Ilmenyx 3/4: configuração'");
        if (tool == ZramTool.Generator)
        {
            s.Add($"cat > {GeneratorConf} <<'ILMENYX_EOF'");
            s.Add(L.T($"{Marker}: criado pelo Ilmenyx (página Otimiza > ZRAM). Para desfazer use Desfazer ou apague este arquivo."));
            s.Add("[zram0]");
            s.Add($"zram-size = min(ram / 2, {MaxSizeMiB})");
            s.Add("compression-algorithm = zstd");
            s.Add("swap-priority = 100");
            s.Add("ILMENYX_EOF");
            s.Add("systemctl daemon-reload");
            s.Add("systemctl start dev-zram0.swap");
        }
        else
        {
            s.Add($"if [ -e {ZramswapConf} ] && ! grep -q '^{Marker}' {ZramswapConf} && [ ! -e {ZramswapBackup} ]; then cp -p {ZramswapConf} {ZramswapBackup}; fi");
            s.Add($"cat > {ZramswapConf} <<'ILMENYX_EOF'");
            s.Add(L.T($"{Marker}: criado pelo Ilmenyx (página Otimiza > ZRAM). O original está em {ZramswapBackup}."));
            s.Add("ALGO=zstd");
            s.Add($"SIZE={size}");
            s.Add("PRIORITY=100");
            s.Add("ILMENYX_EOF");
            s.Add("systemctl enable zramswap.service");
            s.Add("systemctl restart zramswap.service");
        }
        s.Add("echo '== Ilmenyx 4/4: verificação'");
        s.Add("if grep -q '^/dev/zram' /proc/swaps; then grep '^/dev/zram' /proc/swaps; echo 'OK: ZRAM ativo.'; else echo 'ATENÇÃO: ZRAM configurado, mas não ativo agora (vale depois de reiniciar).'; exit 4; fi");
        return Header("ZRAM via " + pkg) + string.Join('\n', s) + "\n";
    }

    public static string UndoScript(DistroFamily family, bool removePackage)
    {
        var tool = ToolFor(family) ?? throw new ArgumentException(L.T("Distribuição sem caminho oficial de ZRAM."));
        string pkg = PackageFor(tool);
        var (_, _, remove) = PackageCommands(family, pkg);
        string conf = tool == ZramTool.Generator ? GeneratorConf : ZramswapConf;
        var s = new List<string>
        {
            "echo '== Ilmenyx 1/3: conferência'",
            $"if ! grep -q '^{Marker}' {conf} 2>/dev/null; then echo 'ABORTADO: {conf} não existe ou não é do Ilmenyx; nada foi alterado.'; exit 3; fi",
            "echo '== Ilmenyx 2/3: desligar o ZRAM do Ilmenyx e remover a configuração'",
        };
        if (tool == ZramTool.Generator)
        {
            s.Add("if grep -q '^/dev/zram0 ' /proc/swaps; then swapoff /dev/zram0 || { echo 'ABORTADO: não foi possível desligar o ZRAM agora (pouca RAM livre?). Feche programas e tente de novo.'; exit 3; }; fi");
            s.Add("systemctl stop systemd-zram-setup@zram0.service || true");
            s.Add($"rm -f {GeneratorConf}");
            s.Add("systemctl daemon-reload");
        }
        else
        {
            s.Add("systemctl stop zramswap.service || { echo 'ABORTADO: não foi possível desligar o ZRAM agora (pouca RAM livre?). Feche programas e tente de novo.'; exit 3; }");
            s.Add("systemctl disable zramswap.service || true");
            s.Add($"if [ -e {ZramswapBackup} ]; then mv -f {ZramswapBackup} {ZramswapConf}; else rm -f {ZramswapConf}; fi");
        }
        s.Add("echo '== Ilmenyx 3/3: pacote'");
        if (removePackage)
            s.Add($"if grep -qx 'package={pkg}' {StateFile} 2>/dev/null; then {remove}; echo 'Pacote {pkg} removido (tinha sido instalado pelo Ilmenyx).'; else echo 'Pacote {pkg} mantido: não foi instalado pelo Ilmenyx.'; fi; rm -f {StateFile}");
        else
            s.Add($"echo 'Pacote {pkg} mantido.'; if ! grep -qx 'package={pkg}' {StateFile} 2>/dev/null; then rm -f {StateFile}; fi");
        s.Add("if grep -q '^/dev/zram' /proc/swaps; then echo 'ATENÇÃO: ainda há ZRAM ativo (de outra configuração).'; else echo 'OK: ZRAM do Ilmenyx desligado.'; fi");
        return Header(L.T("desfazer ZRAM")) + string.Join('\n', s) + "\n";
    }

    // ---- plan ----

    public static ZramPlan Plan(ZramFacts f)
    {
        var findings = new List<string>();
        var tool = ToolFor(f.Distro.Family);
        string distro = f.Distro.Name.Length > 0 ? f.Distro.Name : L.T("distribuição desconhecida");
        findings.Add("Sistema: " + distro + (tool is { } t ? L.T($" · caminho oficial: {PackageFor(t)}") : L.T(" · sem caminho oficial conhecido")));
        if (f.MemTotalBytes is { } ram) findings.Add("RAM: " + GiB(ram));
        var zramSwaps = f.Swaps.Where(s => s.Name.StartsWith("/dev/zram", StringComparison.Ordinal)).ToList();
        var otherSwaps = f.Swaps.Except(zramSwaps).ToList();
        findings.Add(f.Swaps.Count == 0 ? L.T("Swap: nenhuma ativa (/proc/swaps vazio).") : L.T("Swap ativa: ") + string.Join("; ", f.Swaps.Select(s => L.T($"{s.Name} ({s.Type}, {GiB(s.SizeKiB * 1024)}, prioridade {s.Priority})"))));
        foreach (var d in f.Devices.Where(d => d.DiskSizeBytes > 0)) findings.Add(L.T($"Dispositivo {d.Name}: {GiB(d.DiskSizeBytes)}, compressão {d.Algorithm}."));
        if (f.EtcGeneratorConf is not null) findings.Add(GeneratorConf + (Ours(f.EtcGeneratorConf) ? L.T(" (criado pelo Ilmenyx)") : L.T(" (de outro programa ou do administrador)")));
        if (f.VendorGeneratorConf is not null) findings.Add(L.T("/usr/lib/systemd/zram-generator.conf (padrão da distribuição)"));
        foreach (var d in f.GeneratorDropIns) findings.Add("Complemento: " + d);
        if (f.ZramswapDefaults is not null) findings.Add(ZramswapConf + (Ours(f.ZramswapDefaults) ? L.T(" (criado pelo Ilmenyx)") : L.T(" (do pacote zram-tools)")));
        if (f.ZramConfigInstalled) findings.Add(L.T("Pacote zram-config instalado (outro gerenciador de ZRAM do Ubuntu)."));

        bool oursConfig = tool == ZramTool.Generator ? Ours(f.EtcGeneratorConf) : tool == ZramTool.ZramTools && Ours(f.ZramswapDefaults);
        long? size = f.MemTotalBytes is { } r ? SizeMiB(r) : null;
        var undo = new List<ZramAction>();
        if (oursConfig && tool is { } ut)
        {
            string pkg = PackageFor(ut);
            undo.Add(new(ZramActionKind.Undo, L.T("Desfazer o ZRAM do Ilmenyx"), L.T("Desfazer"),
                [L.T("Desliga o ZRAM criado pelo Ilmenyx (os dados nele voltam para a RAM)."), ut == ZramTool.Generator ? L.T($"Apaga {GeneratorConf} e recarrega o systemd.") : L.T($"Para e desativa o serviço zramswap e devolve o {ZramswapConf} original."), L.T($"Mantém o pacote {pkg} instalado.")],
                [L.T("Se a RAM estiver quase cheia, desligar pode falhar; o script para sem mudar nada.")], UndoScript(f.Distro.Family, false)));
            if (InstalledByUs(f, ut))
                undo.Add(new(ZramActionKind.UndoAndRemovePackage, L.T($"Desfazer e remover o pacote {pkg}"), L.T("Desfazer e remover pacote"),
                    [L.T("Faz o mesmo que Desfazer."), L.T($"Remove também o pacote {pkg}, que foi instalado pelo Ilmenyx.")], [L.T("Se a RAM estiver quase cheia, desligar pode falhar; o script para sem mudar nada.")], UndoScript(f.Distro.Family, true)));
        }

        if (zramSwaps.Count > 0)
        {
            string what = string.Join(", ", zramSwaps.Select(s => $"{s.Name} ({GiB(s.SizeKiB * 1024)})"));
            return oursConfig
                ? new(ZramState.ActiveOurs, L.T("ZRAM ativo (configurado pelo Ilmenyx)"), L.T($"{what}. Para voltar como era, use Desfazer."), findings, undo, tool, size)
                : new(ZramState.Active, L.T("ZRAM já está ativo"), L.T($"{what}. O sistema já usa memória comprimida; nada a fazer."), findings, [], tool, size);
        }
        if (tool is null) return new(ZramState.Unsupported, L.T("Sem caminho oficial de ZRAM conhecido"), L.T($"O Ilmenyx só configura ZRAM pelo caminho oficial de Ubuntu/Mint, Debian, Fedora, Arch e openSUSE. Esta distribuição ({distro}) não foi reconhecida."), findings, [], null, size);
        if (oursConfig) return new(ZramState.ConfiguredInactive, L.T("ZRAM configurado, mas não ativo"), L.T("A configuração do Ilmenyx existe, mas o ZRAM não está ligado agora. Reinicie o PC ou use Desfazer."), findings, undo, tool, size);
        bool foreign = f.ZramConfigInstalled || f.EtcGeneratorConf is not null || f.GeneratorDropIns.Count > 0;
        if (foreign) return new(ZramState.ForeignConfig, L.T("Existe outra configuração de ZRAM"), L.T("Há uma configuração de ZRAM feita por outro programa ou pelo administrador, mas o ZRAM não está ativo. O Ilmenyx não mexe nela; confira-a ou peça ajuda a quem a criou."), findings, [], tool, size);
        if (f.MemTotalBytes is not { } mem) return new(ZramState.Unknown, L.T("Não foi possível ler a RAM"), L.T("Sem o tamanho da RAM (/proc/meminfo) o Ilmenyx não recomenda nada."), findings, [], tool, null);
        if (mem > MaxRamBytes) return new(ZramState.NotNeeded, L.T("ZRAM não é necessário aqui"), L.T($"Com {GiB(mem)} de RAM, faltar memória em jogos é raro. O Ilmenyx só recomenda ZRAM até 16 GB."), findings, [], tool, size);

        string package = PackageFor(tool.Value);
        bool needsInstall = tool == ZramTool.Generator ? !f.GeneratorInstalled : !f.ZramToolsInstalled;
        var apply = new ZramAction(ZramActionKind.Apply, L.T("Ligar ZRAM (memória comprimida)"), L.T("LIGAR ZRAM"),
            [
                needsInstall ? L.T($"Instala o pacote oficial {package} da sua distribuição (sem perguntas no terminal).") : L.T($"Usa o pacote {package}, que já está instalado."),
                tool == ZramTool.Generator ? L.T($"Cria {GeneratorConf}: ZRAM de até metade da RAM (máximo 8 GiB), compressão zstd, prioridade acima da swap atual.") : L.T($"Guarda o {ZramswapConf} atual e grava um novo: {size} MiB (metade da RAM, máximo 8 GiB), compressão zstd, prioridade 100."),
                L.T("Liga o ZRAM agora e confere em /proc/swaps. Vale também depois de reiniciar."),
            ],
            [
                L.T("O ZRAM usa um pouco de CPU para comprimir; em PCs muito fracos isso pode pesar em momentos de falta de RAM."),
                otherSwaps.Count > 0 ? L.T("A swap atual (") + string.Join(", ", otherSwaps.Select(s => s.Name)) + L.T(") continua ligada, com prioridade menor.") : L.T("Não havia swap: com o ZRAM, o sistema passa a ter uma."),
                needsInstall ? L.T("Instalar o pacote precisa de internet (espelhos da sua distribuição).") : L.T("Sem download."),
            ], ApplyScript(f.Distro.Family, size!.Value));
        return new(ZramState.Recommended, L.T("Recomendado: ligar ZRAM"),
            L.T($"Com {GiB(mem)} de RAM, jogo + navegador abertos podem encher a memória e o PC travar. O ZRAM cria uma área de troca comprimida dentro da própria RAM (cerca de 2 a 3 vezes mais espaço útil) e evita esses travamentos. Nada muda sem o seu clique e a senha do sistema."),
            findings, [apply], tool, size);
    }
}
