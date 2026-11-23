// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class AchievementsTests
{
    [Fact]
    public void Steam_public_list_keeps_order_names_icons_and_rate_and_drops_unsafe_icon_names()
    {
        const string json = """{"response":{"achievements":[{"internal_name":"CHARMED","localized_name":"Afortunado","localized_desc":"Adquira seu primeiro Amuleto","icon":"6d15.jpg","icon_gray":"../x.jpg","hidden":false,"player_percent_unlocked":"76.7"},{"internal_name":"SECRET","localized_name":"S","hidden":true}]}}""";
        var list = Achievements.ParseSteamList(json);
        Assert.Equal(["CHARMED", "SECRET"], list.Select(a => a.Id));
        Assert.Equal(("Afortunado", "6d15.jpg", "", 76.7), (list[0].Name, list[0].Icon, list[0].IconGray, list[0].GlobalPercent!.Value));
        Assert.True(list[1].Hidden);
        Assert.Empty(Achievements.ParseSteamList("<html>"));
    }

    [Fact]
    public void Emulator_saves_codex_ini_and_goldberg_json()
    {
        var codex = Achievements.ParseCodexIni("[SteamAchievements]\nCount=2\n00000=CHARMED\n[CHARMED]\nAchieved=1\nCurProgress=0\nUnlockTime=1700000000\n[ZOTE]\nAchieved=0\n[HORNET_1]\r\nAchieved=1\r\nUnlockTime=0\r\n");
        Assert.Equal(["CHARMED", "HORNET_1"], codex.Keys.Order());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), codex["CHARMED"].At);
        Assert.Null(codex["HORNET_1"].At);
        var gold = Achievements.ParseGoldbergJson("""{"A":{"earned":true,"earned_time":1700000001},"B":{"earned":false,"earned_time":0},"C":5}""");
        Assert.Equal(["A"], gold.Keys);
        Assert.Empty(Achievements.ParseGoldbergJson("not json"));
        Assert.Equal("367520", Achievements.ParseAppId("###\n#AppId=1\n[Settings]\nAppId=367520\n"));
        Assert.Equal("480", Achievements.ParseAppId("480\n"));
        Assert.Null(Achievements.ParseAppId("AppId=0"));
        Assert.Equal("Nailsmith kill", Achievements.Readable("NAILSMITH_KILL"));
    }

    [Fact]
    public void Steam_local_schema_and_player_stats_in_binary_keyvalues()
    {
        static void BitA(W b) => b.Str("name", "ACH_A").Map("display", d => d.Map("name", n => n.Str("english", "Home").Str("brazilian", "Casa")).Map("desc", n => n.Str("english", "Desc")).Int("hidden", 0).Str("icon", "a.jpg").Str("icon_gray", "ag.jpg"));
        static void BitB(W b) => b.Str("name", "ACH_B").Map("display", d => d.Map("name", n => n.Str("english", "Second")).Int("hidden", 1));
        static void Stats(W st) => st.Map("1", x => x.Str("type", "INT").Str("name", "stat_1")).Map("2", x => x.Str("type", "ACHIEVEMENTS").Map("bits", bits => bits.Map("0", BitA).Map("1", BitB)));
        byte[] schema = Kv(w => w.Map("1262540", m => m.Map("stats", Stats)));
        var (defs, bits) = Achievements.ParseSteamSchema(schema, "brazilian");
        Assert.Equal(["ACH_A", "ACH_B"], defs.Select(d => d.Id));
        Assert.Equal(("Casa", "Desc", "a.jpg", false), (defs[0].Name, defs[0].Description, defs[0].Icon, defs[0].Hidden));
        Assert.Equal(("Second", true), (defs[1].Name, defs[1].Hidden));
        Assert.Equal(("2", 1), bits["ACH_B"]);

        byte[] stats = Kv(w => w.Map("cache", c => c.Int("crc", 0).Map("2", s => s.Int("data", 0b10).Map("AchievementTimes", t => t.Int("1", 1700000002)))));
        var got = Achievements.ParseSteamStats(stats, bits);
        Assert.Equal(["ACH_B"], got.Keys);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000002), got["ACH_B"].At);
        Assert.Empty(Achievements.ParseSteamStats([0, 1, 2], bits)); // broken file
    }

    [Fact]
    public async Task Emulator_game_is_found_read_from_its_prefix_and_watched_for_new_unlocks()
    {
        string root = Directory.CreateTempSubdirectory("ilx-ach").FullName;
        try
        {
            string game = Path.Combine(root, "Hollow Knight"), plugins = Path.Combine(game, "hollow_knight_Data", "Plugins", "x86_64"), exe = Path.Combine(game, "hollow_knight.exe");
            Directory.CreateDirectory(plugins); File.WriteAllText(exe, "MZ");
            var g = new LibGame("Manual", exe, "Hollow Knight", game, exe);
            Assert.Null(AchievementFiles.Find(g, null, [], Path.Combine(root, "prefixes"))); // no emulator: nothing to read
            File.WriteAllText(Path.Combine(plugins, "steam_emu.ini"), "[Settings]\nAppId=367520\n");
            var source = AchievementFiles.Find(g, new GameProfile(), [], Path.Combine(root, "prefixes"))!;
            Assert.Equal(("367520", AchievementOrigin.Emulator), (source.AppId, source.Origin));
            string save = source.Files.First(f => f.Contains("CODEX"));
            Assert.StartsWith(Path.Combine(root, "prefixes", "hollow-knight", "drive_c"), save);

            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "[CHARMED]\nAchieved=1\nUnlockTime=1700000000\n");
            var view = await AchievementFiles.LoadAsync(source, [], "brazilian",
                (_, _, _) => Task.FromResult<IReadOnlyList<AchievementDef>>([new("CHARMED", "Afortunado", "", "", "", false), new("ZOTE", "Zote", "", "", "", false)]), CancellationToken.None);
            Assert.Equal((2, 1), (view.Defs.Count, view.Unlocked));
            var offline = await AchievementFiles.LoadAsync(source, [], "brazilian", (_, _, _) => throw new HttpRequestException("offline"), CancellationToken.None);
            Assert.Equal(["Charmed"], offline.Defs.Select(d => d.Name)); // ids only, readable

            var news = new List<string>(); using var stop = new CancellationTokenSource();
            var watching = AchievementFiles.WatchAsync(source, () => view.Defs, d => { news.Add(d.Name); return Task.CompletedTask; }, stop.Token, TimeSpan.FromMilliseconds(20));
            await Task.Delay(100);
            File.WriteAllText(save, "[CHARMED]\nAchieved=1\nUnlockTime=1700000000\n[ZOTE]\nAchieved=1\nUnlockTime=1700000100\n");
            for (int i = 0; i < 100 && news.Count == 0; i++) await Task.Delay(20);
            stop.Cancel(); await watching;
            Assert.Equal(["Zote"], news); // CHARMED was already there when the game started
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class W(BinaryWriter b)
    {
        public W Map(string k, Action<W> inner) { b.Write((byte)0); Key(k); inner(this); b.Write((byte)8); return this; }
        public W Str(string k, string v) { b.Write((byte)1); Key(k); Key(v); return this; }
        public W Int(string k, int v) { b.Write((byte)2); Key(k); b.Write(v); return this; }
        private void Key(string s) { b.Write(Encoding.UTF8.GetBytes(s)); b.Write((byte)0); }
    }

    private static byte[] Kv(Action<W> build)
    {
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        build(new W(bw)); bw.Write((byte)8); bw.Flush();
        return ms.ToArray();
    }
}
