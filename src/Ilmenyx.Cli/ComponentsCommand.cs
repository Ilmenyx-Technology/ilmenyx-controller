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
        if (args.Length > 1 && !check) { await output.WriteLineAsync(L.T("Uso: ilmenyx components [--check-openrgb]")); return 2; }
        await output.WriteLineAsync(L.T("Programas integrados (sistema tem prioridade; o pacote é o reserva):"));
        foreach (var (name, purpose) in new[] { ("mangohud", L.T("overlay de FPS (MangoHud)")), ("gamemoderun", L.T("modo jogo")), ("openrgb", L.T("iluminação RGB (OpenRGB)")) })
        {
            string? system = BoundedProcess.FindSystem(name), bundled = BundledComponents.IsProvided(name) ? BundledComponents.Resolve(name) : null;
            string source = system is not null ? L.T($"sistema ({system})") : bundled is not null ? (name == "gamemoderun" ? L.T($"embutido no app ({bundled})") : L.T($"pacote ({bundled})")) : L.T("AUSENTE");
            await output.WriteLineAsync($"  {name,-12} {source}  — {purpose}");
        }
        await output.WriteLineAsync($"  flatpak      {(BoundedProcess.FindSystem("flatpak") is { } f ? L.T("sistema (") + f + ")" : L.T("AUSENTE (componente do sistema; não embutível)"))}");
        await output.WriteLineAsync(L.T($"Pasta de componentes do pacote: {BundledComponents.Root() ?? L.T("não encontrada")}"));
        if (!check) return 0;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(90));
            await using var server = await OpenRgbServer.StartAsync(6798, TimeSpan.FromSeconds(60), limit.Token);
            await using var client = await OpenRgbClient.ConnectAsync("127.0.0.1", 6798, limit.Token);
            var devices = await client.ListAsync(limit.Token);
            await output.WriteLineAsync(L.T($"OpenRGB: servidor iniciado em 127.0.0.1:6798 ({server.Program}), protocolo {client.Protocol}, {devices.Count} dispositivo(s) com LEDs. Encerrando."));
            return 0;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OpenRgbException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { await output.WriteLineAsync(L.T("OpenRGB: falha — ") + e.Message); return 1; }
    }
}
