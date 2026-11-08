// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Plain-language view of the driver inventory: one card per device category, a status per device and at most one recommended action.
// Organisation inspired by Windows Device Manager (categories), Ubuntu "Drivers adicionais" / Mint Driver Manager (free vs proprietary,
// one recommended choice) and GPU vendor apps (status first, details later). Only facts read from sysfs; nothing is installed here.

namespace Ilmenyx.Core;

public enum DriverCategory { Video, Network, Bluetooth, Audio, Printing, Other }
public enum DriverHealth { Ok, Info, Attention }
public enum DriverAction { None, OpenNvidiaTab, OpenDriverManager }

public sealed record DriverDevice(DriverRow Row, DriverCategory Category, string Kind, DriverHealth Health, string Badge, string License, string Plain, string Short, DriverAction Action, GpuDriverStatus? Gpu = null);

public sealed record DriverCategoryView(DriverCategory Category, string Title, string Description, string Empty, IReadOnlyList<DriverDevice> Devices)
{
    public DriverHealth Health => Devices.Count == 0 ? DriverHealth.Info : Devices.Max(d => d.Health);
}

public sealed record DriverSummary(int Devices, int Attention, int Unused, string Headline, IReadOnlyList<string> Lines);

public static class DriverOverview
{
    /// <summary>Title, one-line description and empty-state text of each card (in page order).</summary>
    public static readonly IReadOnlyList<(DriverCategory Category, string Title, string Description, string Empty)> Categories =
    [
        (DriverCategory.Video, "Vídeo / placa de vídeo", "Desenha a tela e roda os jogos.", "Nenhuma placa de vídeo PCI encontrada."),
        (DriverCategory.Network, "Rede e Wi-Fi", "Internet por cabo e sem fio.", "Nenhuma placa de rede encontrada."),
        (DriverCategory.Bluetooth, "Bluetooth", "Fones, caixas de som, mouses e controles sem fio.", "Nenhum adaptador Bluetooth encontrado (o PC pode não ter, ou ele está desligado na BIOS ou no modo avião)."),
        (DriverCategory.Audio, "Áudio", "Alto-falantes, fone, microfone e som pelo HDMI.", "Nenhuma placa de som encontrada."),
        (DriverCategory.Printing, "Impressoras e scanners", "Ligados por USB. Os de rede ou Wi-Fi não aparecem aqui: veja nas configurações de impressão do sistema.", "Nenhuma impressora ou scanner USB conectado agora."),
        (DriverCategory.Other, "Outros", "Armazenamento, portas USB, teclado, mouse, câmera e peças internas da placa-mãe.", "Nenhum outro dispositivo."),
    ];

    /// <summary>Kernel modules that are Wi-Fi drivers (USB and PCI), when sysfs has no wireless interface yet (for example, the radio is off).</summary>
    public static readonly string[] WifiDrivers = ["iwlwifi", "ath5k", "ath9k", "ath9k_htc", "ath10k_pci", "ath10k_usb", "ath11k_pci", "ath12k", "brcmfmac", "brcmsmac", "b43", "b43legacy", "wl", "mt7601u", "mt76x0u", "mt76x2u", "mt7921e", "mt7921u", "mt7925e", "mt7996e", "rt2800pci", "rt2800usb", "rt73usb", "rtl8xxxu", "rtl8192cu", "rtl8188ee", "rtl8192ee", "rtl8723be", "rtl8821ae", "rtw88_*", "rtw89_*", "8821cu", "88x2bu", "8812au", "mwifiex_*", "carl9170", "zd1211rw"];
    /// <summary>Ethernet over USB (adapters and phone tethering).</summary>
    public static readonly string[] UsbNetDrivers = ["r8152", "cdc_ether", "cdc_ncm", "rndis_host", "ax88179_178a", "asix", "smsc95xx", "lan78xx", "ipheth"];
    /// <summary>Drivers made by the vendor (closed source) even when the kernel taint does not say so (module not loaded, Flatpak without /sys/module).</summary>
    public static readonly string[] ProprietaryDrivers = ["nvidia", "nvidia_drm", "nvidia_modeset", "wl", "fglrx"];

    private static bool Is(string driver, IEnumerable<string> list) => driver.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Any(d => list.Any(x => x.EndsWith('*') ? d.StartsWith(x[..^1], StringComparison.Ordinal) : d == x));

    private static bool Name(DriverRow r, params string[] words) => words.Any(w => r.Device.Contains(w, StringComparison.OrdinalIgnoreCase));
    private static bool UsbClass(DriverRow r, string cls) => r.Bus == "USB" && r.ClassCode.Split(',').Contains(cls, StringComparer.OrdinalIgnoreCase);
    private static string Pci(DriverRow r) => r.ClassCode.ToLowerInvariant().Replace("0x", "");

    public static DriverCategory CategoryOf(DriverRow r)
    {
        string pci = Pci(r);
        if (r.Class == "DISPLAY" || (r.Bus == "PCI" && pci.StartsWith("03", StringComparison.Ordinal))) return DriverCategory.Video;
        if (Is(r.Driver, ["btusb", "btintel", "btrtl", "btmtk", "hci_uart", "bluetooth"]) || (r.Bus == "PCI" && pci.StartsWith("0d11", StringComparison.Ordinal)) || UsbClass(r, "e0") || (r.Bus != "PCI" && Name(r, "Bluetooth"))) return DriverCategory.Bluetooth;
        if (r.Class == "NET" || r.Wireless || Is(r.Driver, WifiDrivers) || Is(r.Driver, UsbNetDrivers)) return DriverCategory.Network;
        if (Is(r.Driver, ["usblp", "ipp-usb"]) || UsbClass(r, "07") || Name(r, "Printer", "Scanner", "Impressora", "Multifuncional", "LaserJet", "DeskJet", "OfficeJet", "PIXMA", "CanoScan", "ScanJet", "Perfection")) return DriverCategory.Printing;
        if ((r.Class == "MEDIA" && (pci.StartsWith("0401", StringComparison.Ordinal) || pci.StartsWith("0403", StringComparison.Ordinal) || pci.Length == 0)) || Is(r.Driver, ["snd_*", "snd-*"]) || UsbClass(r, "01")) return DriverCategory.Audio;
        return DriverCategory.Other;
    }

    /// <summary>What the device is, in plain words (used mostly in "Outros").</summary>
    public static string KindOf(DriverRow r, DriverCategory c)
    {
        string pci = Pci(r);
        return c switch
        {
            DriverCategory.Video => "Placa de vídeo",
            DriverCategory.Network => r.Wireless || Is(r.Driver, WifiDrivers) || pci.StartsWith("0280", StringComparison.Ordinal) ? "Wi-Fi" : "Rede cabeada",
            DriverCategory.Bluetooth => "Bluetooth",
            DriverCategory.Audio => r.Bus == "USB" ? "Som USB (fone ou placa externa)" : "Placa de som",
            DriverCategory.Printing => "Impressora ou scanner",
            _ when r.Class == "STORAGE" => "Armazenamento (SSD, HD)",
            _ when Is(r.Driver, ["usb-storage", "uas"]) || UsbClass(r, "08") => "Pendrive ou HD externo",
            _ when Is(r.Driver, ["uvcvideo"]) || UsbClass(r, "0e") => "Câmera (webcam)",
            _ when Is(r.Driver, ["usbhid", "hid-generic", "xpad"]) || UsbClass(r, "03") => "Teclado, mouse ou controle",
            _ when UsbClass(r, "09") => "Hub USB",
            _ when r.Class == "USB/BUS" => "Controlador USB ou de barramento",
            _ when r.Class == "MEDIA" => "Multimídia (captura de vídeo)",
            _ when r.Class == "SYSTEM" => "Peça interna da placa-mãe",
            _ when r.Bus == "USB" => "Aparelho USB",
            _ => "Dispositivo",
        };
    }

    public static bool HasDriver(DriverRow r) => r.Driver.Length > 0 && r.Driver != "sem driver";

    public static string LicenseOf(DriverRow r)
    {
        if (!HasDriver(r)) return "—";
        if (r.Flags.Contains("proprietário", StringComparison.Ordinal) || Is(r.Driver, ProprietaryDrivers)) return "Proprietário (do fabricante)";
        if (r.Flags.Contains("fora da árvore", StringComparison.Ordinal)) return "Livre, instalado à parte do kernel";
        return "Livre (vem com o Linux)";
    }

    public static DriverDevice Describe(DriverRow r, GpuDriverStatus? gpu = null)
    {
        var c = CategoryOf(r); string kind = KindOf(r, c), license = LicenseOf(r);
        bool important = c is DriverCategory.Video or DriverCategory.Network or DriverCategory.Bluetooth or DriverCategory.Audio;
        bool nvidia = gpu?.Vendor == "NVIDIA" || (c == DriverCategory.Video && r.Id.StartsWith("10de:", StringComparison.Ordinal));
        DriverDevice D(DriverHealth h, string badge, string plain, string shortText, DriverAction a = DriverAction.None) => new(r, c, kind, h, badge, license, plain, shortText, a, gpu);

        if (nvidia)
        {
            if (r.Driver == "nouveau")
                return D(DriverHealth.Attention, "DRIVER BÁSICO", "Usando o driver livre básico (nouveau): serve para a área de trabalho, mas jogos ficam lentos e a placa gasta mais energia. O driver oficial da NVIDIA resolve.", "usando o driver básico (nouveau)", DriverAction.OpenNvidiaTab);
            if (!HasDriver(r))
                return D(DriverHealth.Attention, "SEM DRIVER", "Nenhum driver em uso: a placa NVIDIA está parada. Instale o driver oficial pela aba Driver NVIDIA.", "sem driver", DriverAction.OpenNvidiaTab);
            if (gpu?.UpdateAvailable == true)
                return D(DriverHealth.Attention, "ATUALIZAÇÃO", $"Driver oficial da NVIDIA em uso ({gpu.Installed}); há uma versão mais nova para esta placa ({gpu.Latest}).", "há versão mais nova do driver", DriverAction.OpenNvidiaTab);
            string version = gpu?.Installed is { Length: > 0 } v ? $" (versão {v})" : "";
            return D(DriverHealth.Ok, "OK", $"Driver oficial da NVIDIA em uso{version}.", "ok", DriverAction.OpenNvidiaTab);
        }
        if (!HasDriver(r))
        {
            if (important)
                return D(DriverHealth.Attention, "SEM DRIVER", "Nenhum driver em uso: esta peça provavelmente não funciona. Pode faltar o firmware (programa interno da peça) ou um driver do fabricante.", "sem driver", DriverAction.OpenDriverManager);
            if (c == DriverCategory.Printing)
                return D(DriverHealth.Info, "PELO SISTEMA", "Sem driver do kernel, o que é normal: impressoras e scanners funcionam pelo sistema de impressão (CUPS) e de digitalização (SANE).", "sem driver do kernel (normal)");
            return D(DriverHealth.Info, "SEM USO", "Nenhum driver em uso. Costuma ser normal para peças internas que o Linux não precisa usar; só investigue se algo não funciona.", "sem driver (normal)");
        }
        if (c == DriverCategory.Video)
            return D(DriverHealth.Ok, "OK", $"Driver livre que já vem com o Linux ({r.Driver}, com o Mesa para 3D e jogos). Ele se atualiza junto com o sistema; não precisa instalar nada.", "ok");
        if (license.StartsWith("Proprietário", StringComparison.Ordinal))
            return D(DriverHealth.Ok, "OK", $"Funcionando com um driver do fabricante ({r.Driver}).", "ok");
        return D(DriverHealth.Ok, "OK", $"Funcionando com o driver {r.Driver}, que já vem com o Linux.", "ok");
    }

    public static IReadOnlyList<DriverCategoryView> Build(IReadOnlyList<DriverRow> rows, IReadOnlyList<GpuDriverStatus> gpus)
    {
        var devices = rows.Select(r => Describe(r, gpus.FirstOrDefault(g => g.PciId.Length > 0 && g.PciId == r.Id && r.Bus == "PCI"))).ToList();
        return Categories.Select(c => new DriverCategoryView(c.Category, c.Title, c.Description, c.Empty,
            devices.Where(d => d.Category == c.Category).OrderByDescending(d => d.Health).ThenBy(d => d.Row.Device, StringComparer.Ordinal).ToList())).ToList();
    }

    public static DriverSummary Summarize(IReadOnlyList<DriverCategoryView> cards)
    {
        var all = cards.SelectMany(c => c.Devices).ToList();
        var attention = cards.SelectMany(c => c.Devices.Where(d => d.Health == DriverHealth.Attention).Select(d => $"{c.Title}: {d.Row.Device} — {d.Short}.")).ToList();
        int unused = all.Count(d => d.Health == DriverHealth.Info);
        string headline = all.Count == 0 ? "Nenhum dispositivo encontrado no sysfs."
            : attention.Count == 0 ? $"Tudo certo: os {all.Count - unused} dispositivo(s) em uso têm driver funcionando."
            : attention.Count == 1 ? "1 ponto precisa de atenção." : $"{attention.Count} pontos precisam de atenção.";
        var lines = new List<string>(attention);
        foreach (var c in cards.Where(c => c.Category != DriverCategory.Other && c.Devices.Count > 0 && c.Health != DriverHealth.Attention)) lines.Add($"{c.Title}: ok.");
        if (unused > 0) lines.Add($"{unused} peça(s) sem driver em uso, o que costuma ser normal (detalhes nos cartões abaixo).");
        return new(all.Count, attention.Count, unused, headline, lines);
    }
}
