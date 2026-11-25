// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>Construction shared by the full shell and the games-only window for the JOGOS pages.</summary>
public static class GamePages
{
    public static CompatServices CompatServicesNow()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var steam = new SteamInfo(home);
        return new CompatServices(new CompatDatabase(CompatPaths.Folder()), new CompatOnline(CompatPaths.Folder()), () => LinuxCompatHardware.Detect(),
            ct => Task.Run<IReadOnlyList<LibGame>>(() => new LinuxGameLibrary(home).Steam().ToList(), ct), steam.GetLaunchOptions, steam.SetLaunchOptions,
            (url, ct) => OperatingSystem.IsLinux() ? DriverTools.OpenUrlAsync(url, ct) : Task.CompletedTask)
        { Profile = id => IlmenyxSettings.Load(IlmenyxSettings.DefaultPath()).GameProfiles.GetValueOrDefault("Steam:" + id) };
    }

    public static IReadOnlyList<(string Id, string Name)> SteamLibraryNow() =>
        !OperatingSystem.IsLinux() ? [] : new LinuxGameLibrary().Steam().Where(g => g.IsSteam && g.Id.All(char.IsAsciiDigit)).DistinctBy(g => g.Id).Select(g => (g.Id, g.Name)).ToList();

    private static IReadOnlyList<string>? steamRoots;
    /// <summary>Cover of a Steam game from Steam's own local cache (portrait first); null when Steam has none. Local files only; call off the UI thread.</summary>
    public static Avalonia.Media.Imaging.Bitmap? SteamArtNow(string appId)
    {
        if (!OperatingSystem.IsLinux()) return null;
        steamRoots ??= new LinuxGameLibrary().SteamRoots().ToList();
        return steamRoots.Select(r => GameArt.Steam(r, appId, false, portrait: true)).FirstOrDefault(b => b is not null);
    }

    /// <summary>What "Meu PC roda isso?" compares with: CPU/GPU names, RAM, free space of the home disk. Read-only, no sudo; call off the UI thread.</summary>
    public static LocalPc LocalPcNow(string cpuModel, long memoryTotal)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        GpuRaw? gpu = null;
        if (OperatingSystem.IsLinux()) try { gpu = new LinuxGpuCollector().Collect(PciIds.Load()).FirstOrDefault().Raw; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        long? free = null;
        try { free = new DriveInfo(home).AvailableFreeSpace; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        return new LocalPc(cpuModel, gpu?.Name ?? "", gpu?.VramBytes ?? 0, gpu?.Integrated ?? false, memoryTotal, free, L.T("sua pasta pessoal"), gpu?.Apis.Contains("Vulkan") == true, gpu is { VendorId.Length: > 0, DeviceId.Length: > 0 } ? $"{gpu.VendorId}:{gpu.DeviceId}" : null);
    }
}
