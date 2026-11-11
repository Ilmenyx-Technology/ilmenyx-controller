// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Disk health (SMART) without typing sudo every time. Linux only lets root ask a disk for its SMART data, so, once and only after the
/// user's click and password, Ilmenyx installs a tiny read-only helper that can do nothing but run `smartctl --health --attributes` on one
/// whole disk, plus a polkit rule that lets the logged-in user run that helper without a password. Undo removes both files.
/// </summary>
public static class SmartAccess
{
    public const string Helper = "/usr/local/libexec/ilmenyx-smart-read";
    public const string Policy = "/usr/share/polkit-1/actions/io.github.ilmenyx_technology.smart-read.policy";

    /// <summary>The helper accepts only sdX / nvmeXnY names and passes fixed, read-only arguments (the same ones as <see cref="SmartctlQuery"/>).</summary>
    internal const string HelperScript = """
        #!/bin/sh
        # Ilmenyx Controller: read-only disk health (SMART) for one whole disk. Installed on the user's request;
        # remove it with "Remover permissão" on the Disks page. It never changes the disk.
        case "$1" in
          sd[a-z]|sd[a-z][a-z]|nvme[0-9]n[0-9]|nvme[0-9][0-9]n[0-9]|nvme[0-9]n[0-9][0-9]) ;;
          *) echo "invalid device" >&2; exit 2 ;;
        esac
        case "$1" in nvme*) type=nvme ;; *) type=ata ;; esac
        for s in /usr/sbin/smartctl /usr/bin/smartctl /sbin/smartctl; do
          [ -x "$s" ] && exec "$s" --json --health --attributes --nocheck=standby,3,5 --device="$type" "/dev/$1"
        done
        echo "smartctl not found" >&2; exit 127
        """;

    internal const string PolicyXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE policyconfig PUBLIC "-//freedesktop//DTD PolicyKit Policy Configuration 1.0//EN" "http://www.freedesktop.org/standards/PolicyKit/1/policyconfig.dtd">
        <policyconfig>
          <vendor>Ilmenyx Technology</vendor>
          <action id="io.github.ilmenyx_technology.smart-read">
            <description>Read disk health (SMART) for Ilmenyx Controller</description>
            <message>Ilmenyx Controller wants to read the health of a disk</message>
            <defaults>
              <allow_any>auth_admin</allow_any>
              <allow_inactive>auth_admin</allow_inactive>
              <allow_active>yes</allow_active>
            </defaults>
            <annotate key="org.freedesktop.policykit.exec.path">/usr/local/libexec/ilmenyx-smart-read</annotate>
          </action>
        </policyconfig>
        """;

    /// <summary>One root script (run by <see cref="PrivilegedFix"/>): writes the two files with fixed content.</summary>
    public static string InstallScript =>
        "set -e; mkdir -p /usr/local/libexec /usr/share/polkit-1/actions; " +
        "cat > " + Helper + " <<'ILMENYX_EOF'\n" + HelperScript + "\nILMENYX_EOF\n" +
        "chmod 755 " + Helper + "; chown root:root " + Helper + "; " +
        "cat > " + Policy + " <<'ILMENYX_EOF'\n" + PolicyXml + "\nILMENYX_EOF\n" +
        "chmod 644 " + Policy;

    public const string UndoScript = "rm -f " + Helper + " " + Policy;

    public static bool Installed => File.Exists(HostSpawn.HostPath(Helper)) && File.Exists(HostSpawn.HostPath(Policy));

    public static bool SmartctlPresent => new[] { "/usr/sbin/smartctl", "/usr/bin/smartctl", "/sbin/smartctl" }.Any(p => File.Exists(HostSpawn.HostPath(p)));

    /// <summary>Command to install smartmontools on the detected distribution family.</summary>
    public static string InstallHint(string distroId, string idLike)
    {
        string all = (distroId + " " + idLike).ToLowerInvariant();
        return all.Contains("arch") ? "sudo pacman -S smartmontools"
            : all.Contains("fedora") || all.Contains("rhel") ? "sudo dnf install smartmontools"
            : all.Contains("suse") ? "sudo zypper install smartmontools"
            : "sudo apt install smartmontools";
    }
}

/// <summary>The SMART query through the installed helper (pkexec, no password for the active user). Same output as <see cref="SmartctlQuery"/>.</summary>
public sealed class HelperSmartctlQuery : ISmartctlQuery
{
    public async Task<SmartctlOutput> QueryAsync(string device, CancellationToken ct)
    {
        if (!SmartctlQuery.ValidDeviceName(device)) throw new ArgumentException(L.T("Informe somente um nome de disco SATA/NVMe, como sda ou nvme0n1."));
        var (code, output) = await BoundedProcess.RunAsync("pkexec", [SmartAccess.Helper, device], TimeSpan.FromSeconds(15), 65536, ct);
        if (code is 126 or 127 && !output.TrimStart().StartsWith('{')) throw new UnauthorizedAccessException(L.T("O sistema não autorizou a leitura da saúde."));
        return new(code, output);
    }
}
