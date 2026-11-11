// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>A disk or partition as lsblk reports it. Sizes in bytes; FsUsed/FsAvail only for mounted file systems.</summary>
public sealed record BlockDevice(string Name, string Path, long Size, string Type, string? FsType, IReadOnlyList<string> Mounts, string? Label,
    string? Transport, string? Model, bool Removable, bool Hotplug, bool Rotational, long? FsUsed, long? FsAvail, string? PartType, bool ReadOnly,
    IReadOnlyList<BlockDevice> Children)
{
    private static readonly string[] SystemMounts = ["/", "/boot", "/boot/efi", "/efi", "/home", "/usr", "/var", "/recovery"];

    /// <summary>Holds the running system (root, boot, home, swap): never unmounted, formatted or ejected from Ilmenyx.</summary>
    public bool IsSystem => Mounts.Any(m => SystemMounts.Contains(m) || m == "[SWAP]") || FsType == "swap" || Children.Any(c => c.IsSystem);

    /// <summary>Pendrives, card readers and USB disks: the only devices Ilmenyx offers to format or eject.</summary>
    public bool IsExternal => Removable || Hotplug || Transport is "usb" or "mmc" || Name.StartsWith("mmcblk", StringComparison.Ordinal);

    public IEnumerable<BlockDevice> Partitions => Children.Where(c => c.Type is "part" or "crypt" or "lvm");
}

/// <summary>Reads the disk tree (lsblk JSON, on the host inside the Flatpak) and runs the few safe actions through udisks2.</summary>
public static partial class BlockDevices
{
    public const string Columns = "NAME,PATH,SIZE,TYPE,FSTYPE,MOUNTPOINTS,LABEL,TRAN,MODEL,RM,HOTPLUG,FSUSED,FSAVAIL,PARTTYPENAME,RO,ROTA";

    [GeneratedRegex("^/dev/[a-z0-9]{2,32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePath();
    [GeneratedRegex("^[A-Za-z0-9 _-]{0,11}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeLabel();

    /// <summary>Real disks only (no loop, zram, ROM drives), with their partitions.</summary>
    public static async Task<IReadOnlyList<BlockDevice>> ReadAsync(CancellationToken ct = default)
    {
        var (code, output) = await BoundedProcess.RunAsync("lsblk", ["-J", "-b", "-o", Columns], TimeSpan.FromSeconds(5), 1 << 20, ct);
        if (code != 0) throw new IOException(L.T("lsblk não respondeu (código ") + code.ToString(CultureInfo.InvariantCulture) + ").");
        return Parse(output).Where(d => d.Type == "disk" && !d.Name.StartsWith("zram", StringComparison.Ordinal)).ToArray();
    }

    /// <summary>Pure: the "blockdevices" tree of `lsblk -J -b`. Numbers and flags may come as JSON numbers/booleans or as strings (older util-linux).</summary>
    public static IReadOnlyList<BlockDevice> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("blockdevices", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray().Select(Device).ToArray();
    }

    private static BlockDevice Device(JsonElement e)
    {
        string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } t ? t.Trim() : null;
        long? N(string k) => e.TryGetProperty(k, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out long n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long n) => n,
            _ => null,
        } : null;
        bool B(string k) => e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "1") || (v.ValueKind == JsonValueKind.Number && v.GetInt32() == 1));
        var mounts = new List<string>();
        if (e.TryGetProperty("mountpoints", out var mp) && mp.ValueKind == JsonValueKind.Array)
            mounts.AddRange(mp.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()!).Where(m => m.Length > 0));
        else if (S("mountpoint") is { } single) mounts.Add(single);
        var children = e.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(Device).ToArray() : [];
        string name = S("name") ?? "";
        return new(name, S("path") ?? "/dev/" + name, N("size") ?? 0, S("type") ?? "", S("fstype"), mounts, S("label"), S("tran"), S("model"),
            B("rm"), B("hotplug"), B("rota"), N("fsused"), N("fsavail"), S("parttypename"), B("ro"), children);
    }

    private static string? Check(BlockDevice device, bool needExternal)
    {
        if (!DevicePath().IsMatch(device.Path)) return L.T("Caminho de dispositivo inválido.");
        if (device.IsSystem) return L.T("Este disco ou partição é do sistema em uso: o Ilmenyx não mexe nele.");
        if (needExternal && !device.IsExternal) return L.T("Só pendrives, cartões e discos USB podem ser formatados ou ejetados por aqui.");
        return null;
    }

    private static async Task<string?> Udisks(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        if (BoundedProcess.FindSystem("udisksctl") is null) return L.T("O udisks2 (udisksctl) não está instalado neste sistema.");
        try
        {
            var (code, output, errors) = await BoundedProcess.RunWithErrorsAsync("udisksctl", args, timeout, 16384, ct);
            return code == 0 ? null : Friendly(errors + "\n" + output);
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return e.Message; }
    }

    /// <summary>udisks messages in plain words for the common cases; otherwise the first line, as is.</summary>
    internal static string Friendly(string output)
    {
        if (output.Contains("target is busy", StringComparison.OrdinalIgnoreCase) || output.Contains("is busy", StringComparison.OrdinalIgnoreCase))
            return L.T("Algum programa ainda está usando arquivos daí. Feche-o (ou a janela do gerenciador de arquivos) e tente de novo.");
        if (output.Contains("NotAuthorized", StringComparison.Ordinal) || output.Contains("not authorized", StringComparison.OrdinalIgnoreCase))
            return L.T("O sistema não autorizou esta ação.");
        if (output.Contains("Dismissed", StringComparison.Ordinal)) return L.T("Cancelado: a senha não foi informada.");
        string first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return first.Length > 0 ? first : L.T("A ação falhou sem mensagem.");
    }

    public static Task<string?> MountAsync(BlockDevice part, CancellationToken ct = default) =>
        Check(part, false) is { } refused ? Task.FromResult<string?>(refused) : Udisks(["mount", "-b", part.Path], TimeSpan.FromSeconds(30), ct);

    public static Task<string?> UnmountAsync(BlockDevice part, CancellationToken ct = default) =>
        Check(part, false) is { } refused ? Task.FromResult<string?>(refused) : Udisks(["unmount", "-b", part.Path], TimeSpan.FromSeconds(30), ct);

    /// <summary>Unmounts every partition, then powers the device off so it can be pulled out safely.</summary>
    public static async Task<string?> EjectAsync(BlockDevice disk, CancellationToken ct = default)
    {
        if (Check(disk, true) is { } refused) return refused;
        foreach (var p in disk.Partitions.Where(p => p.Mounts.Count > 0))
            if (await Udisks(["unmount", "-b", p.Path], TimeSpan.FromSeconds(30), ct) is { } error) return error;
        return await Udisks(["power-off", "-b", disk.Path], TimeSpan.FromSeconds(30), ct);
    }

    public static readonly IReadOnlyList<(string Type, string Name, string Note)> FormatTypes =
    [
        ("exfat", "exFAT", L.T("Recomendado para pendrive: funciona no Linux, Windows, Mac, TVs e videogames, e aceita arquivos grandes.")),
        ("vfat", "FAT32", L.T("Funciona em quase tudo, mas não aceita arquivos maiores que 4 GB.")),
        ("ext4", "ext4", L.T("Só para uso no Linux.")),
        ("ntfs", "NTFS", L.T("Para usar principalmente no Windows.")),
    ];

    /// <summary>
    /// Erases a partition of an external device and creates a new file system there, through udisks2 (Block.Format with tear-down, which
    /// unmounts it first). Only external, non-system partitions; the label is limited to letters, digits, spaces, "_" and "-".
    /// </summary>
    public static async Task<string?> FormatAsync(BlockDevice part, BlockDevice disk, string type, string label, CancellationToken ct = default)
    {
        if ((Check(disk, true) ?? Check(part, false)) is { } refused) return refused;
        if (!disk.Partitions.Any(p => p.Path == part.Path) && part.Path != disk.Path) return L.T("A partição não é deste disco.");
        if (FormatTypes.All(t => t.Type != type)) return L.T("Tipo de formatação desconhecido.");
        if (!SafeLabel().IsMatch(label)) return L.T("Nome inválido: use até 11 letras, números, espaço, _ ou -.");
        if (BoundedProcess.FindSystem("gdbus") is null) return L.T("O gdbus (GLib) não está instalado: não dá para pedir a formatação ao udisks2.");
        string objectPath = "/org/freedesktop/UDisks2/block_devices/" + System.IO.Path.GetFileName(part.Path);
        string options = "{'label': <'" + label + "'>, 'tear-down': <true>, 'update-partition-type': <true>}";
        try
        {
            var (code, output, errors) = await BoundedProcess.RunWithErrorsAsync("gdbus", ["call", "--system", "--dest", "org.freedesktop.UDisks2", "--object-path", objectPath,
                "--timeout", "600", "--method", "org.freedesktop.UDisks2.Block.Format", type, options], TimeSpan.FromMinutes(11), 16384, ct);
            if (code == 0) return null;
            output = errors + "\n" + output;
            if (output.Contains("mkfs", StringComparison.OrdinalIgnoreCase) && output.Contains("not", StringComparison.OrdinalIgnoreCase))
                return L.T("Falta o programa que cria esse tipo de sistema de arquivos (por exemplo exfatprogs, dosfstools ou ntfs-3g). Instale-o e tente de novo.");
            return Friendly(output);
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return e.Message; }
    }

    /// <summary>A partition editor for the system disk (create, delete, resize stay with a dedicated tool): command and name, or null.</summary>
    public static (string Command, string Name)? PartitionEditor() =>
        new[] { ("partitionmanager", "KDE Partition Manager"), ("gparted", "GParted"), ("gnome-disks", "Discos do GNOME") }
            .Select(t => ((string, string)?)t).FirstOrDefault(t => BoundedProcess.FindSystem(t!.Value.Item1) is not null);

    public static string? OpenPartitionEditor()
    {
        if (PartitionEditor() is not { } editor || BoundedProcess.FindSystem(editor.Command) is not { } path) return L.T("Nenhum editor de partições instalado.");
        try { using var p = Process.Start(HostSpawn.Start(path, [], redirect: false)); return null; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { return e.Message; }
    }
}
