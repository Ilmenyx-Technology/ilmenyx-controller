// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Cli;

/// <summary>`ilmenyx components [--check-openrgb]`: where each integrated program comes from (system, package or built-in). The check starts the OpenRGB server on loopback for a few seconds, only when asked.</summary>
public static class ComponentsCommand
{
    public static bool Handles(string[] args) => args is ["components", ..];

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken ct)
    {
        bool check = args.Skip(1).SequenceEqual(["--check-openrgb"]);
        if (args.Length > 1 && !check) { await output.WriteLineAsync("Uso: ilmenyx components [--check-openrgb]"); return 2; }
        await output.WriteLineAsync("Programas integrados (sistema tem prioridade; o pacote é o reserva):");
        foreach (var (name, purpose) in new[] { ("mangohud", "overlay de FPS (MangoHud)"), ("gamemoderun", "modo jogo"), ("openrgb", "iluminação RGB (OpenRGB)") })
        {
            string? system = BoundedProcess.FindSystem(name), bundled = BundledComponents.IsProvided(name) ? BundledComponents.Resolve(name) : null;
            string source = system is not null ? $"sistema ({system})" : bundled is not null ? (name == "gamemoderun" ? $"embutido no app ({bundled})" : $"pacote ({bundled})") : "AUSENTE";
            await output.WriteLineAsync($"  {name,-12} {source}  — {purpose}");
        }
        await output.WriteLineAsync($"  flatpak      {(BoundedProcess.FindSystem("flatpak") is { } f ? "sistema (" + f + ")" : "AUSENTE (componente do sistema; não embutível)")}");
        await output.WriteLineAsync($"Pasta de componentes do pacote: {BundledComponents.Root() ?? "não encontrada"}");
        if (!check) return 0;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(90));
            await using var server = await OpenRgbServer.StartAsync(6798, TimeSpan.FromSeconds(60), limit.Token);
            await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", 6798, limit.Token);
            var devices = await client.ListAsync(limit.Token);
            await output.WriteLineAsync($"OpenRGB: servidor iniciado em 127.0.0.1:6798 ({server.Program}), protocolo {client.Protocol}, {devices.Count} dispositivo(s) com LEDs. Encerrando.");
            return 0;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OpenRgbException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { await output.WriteLineAsync("OpenRGB: falha — " + e.Message); return 1; }
    }
}
