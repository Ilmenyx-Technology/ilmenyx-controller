// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
namespace Ilmenyx.Platform.Linux;

public interface IHwmonFiles
{
    IReadOnlyList<string> Devices();
    IReadOnlyList<string> Inputs(string device);
    Task<string> ReadAsync(string device, string attribute, CancellationToken ct);
}

public sealed class HwmonFiles : IHwmonFiles
{
    private const string Root = "/sys/class/hwmon";
    public IReadOnlyList<string> Devices() => Directory.EnumerateDirectories(Root).Take(65).Select(Path.GetFileName).Cast<string>().ToArray();
    public IReadOnlyList<string> Inputs(string device)
    {
        if (!HwmonParser.ValidDevice(device)) throw new IOException("Caminho hwmon inválido.");
        return Directory.EnumerateFiles(Path.Combine(Root, device), "*_input")
            .Take(129).Select(Path.GetFileName).Cast<string>().ToArray();
    }
    public async Task<string> ReadAsync(string device, string attribute, CancellationToken ct)
    {
        if (!HwmonParser.ValidDevice(device) || attribute.Length > 64 || attribute.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new IOException("Caminho hwmon inválido.");
        using var reader = new StreamReader(new FileStream(Path.Combine(Root, device, attribute), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true));
        var buffer = new char[4097];
        int length = 0, count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(length), ct)) > 0)
        {
            length += count;
            if (length > 4096) throw new IOException("Atributo hwmon excede 4096 caracteres.");
        }
        return new string(buffer, 0, length);
    }
}
