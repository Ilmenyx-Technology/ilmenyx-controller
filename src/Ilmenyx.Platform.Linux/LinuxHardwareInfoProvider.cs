using System.Runtime.InteropServices;
using Ilmenyx.Contracts;
namespace Ilmenyx.Platform.Linux;

public sealed class LinuxHardwareInfoProvider(ILinuxFiles files, ISensorProvider? sensors = null, IGpuInfoProvider? gpus = null) : IHardwareInfoProvider
{
    private static bool Recoverable(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private async Task<(string? Text, string? Error)> Read(string path, CancellationToken ct)
    {
        try { return (await files.ReadAsync(path, ct), null); }
        catch (Exception e) when (Recoverable(e)) { return (null, e is UnauthorizedAccessException ? "Permissão negada." : "Fonte ausente ou inacessível."); }
    }
    public async Task<HardwareSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        var cpuText = await Read("/proc/cpuinfo", ct);
        var memText = await Read("/proc/meminfo", ct);
        var osText = await Read(HostSpawn.HostPath("/etc/os-release"), ct);
        var kernelText = await Read("/proc/sys/kernel/osrelease", ct);
        var cpu = LinuxParsers.Cpu(cpuText.Text ?? "");
        if (cpuText.Error is { } ce) cpu = new(Reading<string>.Missing("", "/proc/cpuinfo", ce), Reading<int?>.Missing("logical processors", "/proc/cpuinfo", ce));
        var memory = LinuxParsers.Memory(memText.Text ?? "");
        if (memText.Error is { } me) memory = new(Reading<long?>.Missing("B", "/proc/meminfo", me), Reading<long?>.Missing("B", "/proc/meminfo", me));
        var os = LinuxParsers.OsName(osText.Text ?? "");
        var kernel = LinuxParsers.Clean(kernelText.Text ?? "");
        Reading<IReadOnlyList<DiskInfo>> disks;
        try
        {
            ct.ThrowIfCancellationRequested();
            var names = files.BlockDevices();
            if (names.Count > 256) throw new IOException("Limite de dispositivos excedido.");
            var list = new List<DiskInfo>();
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (name.Length == 0 || name.Length > 128 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')) throw new IOException("Nome inválido.");
                if (name is "." or "..") throw new IOException("Nome inválido.");
                var source = $"/sys/block/{name}/size";
                var size = await Read(source, ct);
                var rotational = await Read($"/sys/block/{name}/queue/rotational", ct);
                var bus = LinuxParsers.DiskBus(name, files.ResolveLink("/sys/block/" + name));
                list.Add(new(name, size.Text is { } t ? LinuxParsers.Capacity(t, source) : Reading<long?>.Missing("B", source, size.Error!),
                    LinuxParsers.DiskKindFor(name, rotational.Text, $"/sys/block/{name}/queue/rotational", bus), bus));
            }
            disks = list.Count > 0 ? Reading<IReadOnlyList<DiskInfo>>.Available(list, "devices", "/sys/block") : Reading<IReadOnlyList<DiskInfo>>.Missing("devices", "/sys/block", "Nenhum dispositivo visível.");
        }
        catch (Exception e) when (Recoverable(e)) { disks = Reading<IReadOnlyList<DiskInfo>>.Missing("devices", "/sys/block", "Inventário inacessível ou inválido."); }
        var sensorSnapshot = sensors is null ? null : await sensors.CollectAsync(ct);
        var gpuSnapshot = gpus is null ? null : await gpus.CollectAsync(ct);
        var capabilities = new List<Capability>
        {
            new("cpu", cpu.Name.Status == "available" && cpu.LogicalProcessors.Status == "available" ? "available" : "unavailable", "Identificação: /proc/cpuinfo; contagem visível, não núcleos físicos."),
            new("memory", memory.Total.Status == "available" && memory.Available.Status == "available" ? "available" : "unavailable", "MemTotal e MemAvailable de /proc/meminfo."),
            new("disks", disks.Status, disks.Reason ?? "Dispositivos de bloco do kernel; inclui virtuais; capacidade não é espaço livre."),
            new("sensors", sensorSnapshot?.Status ?? "unavailable", sensorSnapshot?.Reason ?? (sensorSnapshot is null ? "Leitor de sensores não configurado." : "Leituras brutas hwmon/thermal/power_supply; sem inferência de saúde.")),
            new("smart", "partial", "CLI smart: consulta somente leitura por disco explícito; exige --allow-device-query e smartctl opcional. Nenhuma coleta automática."),
            new("gpu", gpuSnapshot?.Status ?? "unavailable", gpuSnapshot?.Reason ?? (gpuSnapshot is null ? "Leitor DRM não configurado." : "Inventário DRM/PCI disponível; telemetria depende do driver.")),
            new("html", "available", "CLI info --html e UI: relatório offline de snapshot; sem coleta ou carga adicional."),
            new("json", "available", "Snapshot local, sem upload; destino novo escolhido pelo usuário."),
            new("cpu-renderer-validation", "available", "CLI e UI: validação reduzida sem score; carga completa somente na CLI com opt-in e não validada."),
            new("memory-algorithm-validation", "available", "CLI/UI: algoritmos em buffers pequenos, sem latência, GB/s, score ou stress."),
            new("gamesim-validation", "available", "CLI/UI: simulação sintética reduzida por passos fixos, sem FPS, score ou perfil completo."),
            new("benchmarks", "partial", "Renderizador CPU experimental, validações reduzidas de memória e GameSim. Scores não comparáveis; demais benchmarks indisponíveis.")
        };
        foreach (var feature in new[] { "overlay", "windows-backend" }) capabilities.Add(new(feature, "unavailable", "Não implementado na Fase 1."));
        ct.ThrowIfCancellationRequested();
        return new("1.3", "0.30.0", DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription,
            os is null ? Reading<string>.Missing("", "/etc/os-release", osText.Error ?? "PRETTY_NAME inválido ou ausente.") : Reading<string>.Available(os, "", "/etc/os-release"),
            kernel.Length == 0 ? Reading<string>.Missing("", "/proc/sys/kernel/osrelease", kernelText.Error ?? "Kernel não informado.") : Reading<string>.Available(kernel, "", "/proc/sys/kernel/osrelease"),
            cpu, memory, disks, capabilities, new[] { "Snapshot somente leitura; não mede desempenho ou saúde.", "Dados refletem /proc e /sys visíveis; contêiner/cgroup/afinidade podem limitar recursos utilizáveis.", "Sem coleta dedicada de hostname, serial, UUID, IP ou machine-id; rótulos livres podem conter identificadores. Revise antes de compartilhar." }, sensorSnapshot, gpuSnapshot);
    }
}
