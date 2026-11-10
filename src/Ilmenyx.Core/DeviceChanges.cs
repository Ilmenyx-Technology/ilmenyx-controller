// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from the device notices of RESET EXPLODE (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
namespace Ilmenyx.Core;

public sealed record WatchedUsb(string Id, string Name);
public sealed record WatchedMount(string MountPoint, string Title);
public sealed record DeviceSnapshot(IReadOnlyList<WatchedUsb> Usb, IReadOnlyList<WatchedMount> Mounts);
public sealed record ToastMessage(string Title, string Text);

/// <summary>Difference between two device snapshots as user-facing notices. The first snapshot is only a baseline (no notices at startup).</summary>
public static class DeviceChanges
{
    public const int MaxToasts = 4;

    public static IReadOnlyList<ToastMessage> Diff(DeviceSnapshot? before, DeviceSnapshot now)
    {
        if (before is null) return [];
        var list = new List<ToastMessage>();
        var oldMounts = before.Mounts.ToDictionary(m => m.MountPoint, StringComparer.Ordinal);
        var newMounts = now.Mounts.ToDictionary(m => m.MountPoint, StringComparer.Ordinal);
        foreach (var m in now.Mounts) if (!oldMounts.ContainsKey(m.MountPoint)) list.Add(new(L.T("Unidade conectada"), m.Title));
        foreach (var m in before.Mounts) if (!newMounts.ContainsKey(m.MountPoint)) list.Add(new(L.T("Unidade removida"), L.T($"{m.Title} pode ser desconectada.")));
        var oldUsb = before.Usb.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var newUsb = now.Usb.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var u in now.Usb) if (!oldUsb.Contains(u.Id)) list.Add(new(L.T("Dispositivo USB conectado"), u.Name));
        foreach (var u in before.Usb) if (!newUsb.Contains(u.Id)) list.Add(new(L.T("Dispositivo USB desconectado"), u.Name));
        return list.Take(MaxToasts).ToList();
    }
}
