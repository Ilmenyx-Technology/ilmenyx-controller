// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the chip table and diagnosis idea of the original MakerDrivers (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

/// <summary>USB-serial chip family: how to recognise it, the Linux kernel module(s) that drive it and the official page.</summary>
public sealed record SerialChip(string Name, string[] Vids, string[] Modules, string OfficialUrl);

public sealed record SerialPortInfo(string Node, string Vid, string Pid, string Product, string Maker, string BoundDriver, bool? Accessible, string? Group);
public sealed record SerialEnvironment(bool BrlttyRunning, bool ModemManagerRunning, bool UserInSerialGroup, string SerialGroup, Func<string, (bool Available, bool Loaded)> Module);
public sealed record SerialDiagnosisResult(bool Ok, string Verdict, string Advice);

public static class SerialDiagnosis
{
    public static readonly SerialChip[] Chips =
    [
        new("CH340/CH341", ["1A86"], ["ch341"], "https://www.wch-ic.com/downloads/CH341SER_EXE.html"),
        new("CP210x", ["10C4"], ["cp210x"], "https://www.silabs.com/developer-tools/usb-to-uart-bridge-vcp-drivers"),
        new("FTDI", ["0403"], ["ftdi_sio"], "https://ftdichip.com/drivers/vcp-drivers/"),
        new("Prolific PL2303", ["067B"], ["pl2303"], "https://www.prolific.com.tw/"),
        new(L.T("Arduino (CDC ACM)"), ["2341", "2A03"], ["cdc_acm"], "https://www.arduino.cc/en/software"),
        new(L.T("Espressif (USB nativo)"), ["303A"], ["cdc_acm"], "https://docs.espressif.com/projects/esp-idf/en/latest/esp32/api-guides/usb-serial-jtag-console.html"),
    ];

    public static SerialChip? ChipByVid(string? vid) => vid is null ? null : Chips.FirstOrDefault(c => c.Vids.Contains(vid, StringComparer.OrdinalIgnoreCase));

    public static SerialDiagnosisResult Diagnose(SerialPortInfo p, SerialEnvironment env)
    {
        var chip = ChipByVid(p.Vid);
        if (p.BoundDriver.Length == 0)
        {
            string module = chip?.Modules[0] ?? "cdc_acm";
            var (available, loaded) = env.Module(module);
            if (env.BrlttyRunning && chip?.Name.StartsWith("CH34", StringComparison.Ordinal) == true)
                return new(false, L.T("Possível captura pelo brltty"), L.T("O brltty (leitor de braile) costuma capturar adaptadores CH340/CH341, que somem de /dev. Se você não usa braile: `sudo systemctl disable --now brltty` e remova o pacote brltty; reconecte o dispositivo."));
            if (available && !loaded) return new(false, L.T($"Módulo {module} não carregado"), L.T($"O kernel tem o módulo {module}, mas ele não está carregado: `sudo modprobe {module}` e reconecte o dispositivo."));
            if (!available) return new(false, L.T($"Kernel sem o módulo {module}"), L.T($"Este kernel não traz o módulo {module}. Instale o pacote de módulos extras do seu kernel (por exemplo linux-modules-extra no Ubuntu) ou o driver do fabricante em {chip?.OfficialUrl ?? L.T("sua página oficial")}."));
            return new(false, L.T("Sem driver associado"), L.T("O módulo está carregado mas não assumiu este dispositivo; reconecte o cabo e confira `dmesg`."));
        }
        if (p.Accessible == false || (p.Accessible is null && !env.UserInSerialGroup))
            return new(false, L.T($"Sem permissão de acesso à porta ({p.Node})"), L.T($"Seu usuário não consegue abrir a porta. Adicione-se ao grupo '{(p.Group is { Length: > 0 } g ? g : env.SerialGroup)}' com `sudo usermod -aG {(p.Group is { Length: > 0 } g2 ? g2 : env.SerialGroup)} $USER` e faça logout/login (o Ilmenyx não altera grupos)."));
        string advice = env.ModemManagerRunning
            ? L.T("Porta acessível. O ModemManager está ativo e pode sondar a porta durante o upload (placas Arduino/ESP); se uploads falharem, crie uma regra udev ID_MM_DEVICE_IGNORE=1 para este VID:PID.")
            : L.T("Porta acessível e driver associado.");
        return new(true, L.T("Funcionando"), advice);
    }
}
