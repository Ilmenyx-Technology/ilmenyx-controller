// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DocumentPortalTests
{
    private const string Alias = "/run/user/1000/doc/At3ELNPWAiVtWHNBgomNmw/Game.exe", Real = "/mnt/HDD/Jogos/My Game/Game.exe";

    [Fact]
    public void Alias_resolves_to_the_real_path_only_when_the_portal_says_and_the_file_is_reachable()
    {
        Assert.Equal(Real, DocumentPortal.HostPath(Alias, _ => Real));
        Assert.Equal(Alias, DocumentPortal.HostPath(Alias, _ => null));
        Assert.Equal(Alias, DocumentPortal.HostPath(Alias, _ => "relative/path"));
        Assert.Equal("/home/u/Game.exe", DocumentPortal.HostPath("/home/u/Game.exe", _ => throw new InvalidOperationException("not an alias: never read")));
        Assert.Equal(Real, DocumentPortal.RealPath(Alias, _ => Real, _ => true));
        Assert.Equal(Alias, DocumentPortal.RealPath(Alias, _ => Real, _ => false));
    }

    [Fact]
    public void Migrate_moves_exe_name_and_profile_and_keeps_a_name_the_user_chose()
    {
        var s = new IlmenyxSettings();
        s.CustomGames.Add(new() { Name = "At3ELNPWAiVtWHNBgomNmw", Exe = Alias });
        s.CustomGames.Add(new() { Name = "Mortal Kombat X", Exe = "/run/user/1000/doc/mhj/MK10.exe" });
        s.CustomGames.Add(new() { Name = "Fica", Exe = "/run/user/1000/doc/zzz/Lost.exe" });
        s.GameProfiles["Manual:" + Alias] = new GameProfile { ProtonGameId = "42" };

        Assert.True(DocumentPortal.Migrate(s, p => p == Alias ? Real : p.EndsWith("MK10.exe") ? "/mnt/MK/MK10.exe" : p, _ => "My Game"));

        Assert.Equal(["My Game", "Mortal Kombat X", "Fica"], s.CustomGames.Select(c => c.Name));
        Assert.Equal([Real, "/mnt/MK/MK10.exe", "/run/user/1000/doc/zzz/Lost.exe"], s.CustomGames.Select(c => c.Exe));
        Assert.Equal("42", s.GameProfiles["Manual:" + Real].ProtonGameId);
        Assert.False(s.GameProfiles.ContainsKey("Manual:" + Alias));
        Assert.False(DocumentPortal.Migrate(s, p => p, _ => ""));
    }
}
