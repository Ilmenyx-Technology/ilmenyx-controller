// Bounded test oracle only, using the MIT upstream source in a temporary directory.
// Original copyright (c) 2026 Gustavo (GustavoReset), MIT; see LICENSE.
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ResetExplode;

if (args.Length != 1 || GameSimBench.Entities != 256) throw new ArgumentException("Only the reduced oracle is allowed.");
var cases = new List<object>();
foreach (int threads in new[] { 1, 2, 4 })
foreach (string scenario in new[] { "seeded", "edge-cases" })
foreach (int steps in scenario == "seeded" ? new[] { 0, 1, 16 } : new[] { 1 })
{
    var sim = new GameSimBench(); Call("Reset");
    if (scenario == "edge-cases")
    {
        Set(0, 500, 500, 0, 0, 20); Set(1, 501, 500, 0, 0, .001f); Set(2, 499, 500, 0, 0, 100);
        Set(3, 1001, 500, 40, 0, 100); Set(4, 999, 999, 100, 100, 100); Set(5, 200, -1, 0, -40, 100);
        Set(6, 500, 501, 0, 0, 100); Set(7, 500, 500, 0, 0, 100);
    }
    sim.OracleThreads = threads;
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    for (int frame = 0; frame < steps; frame++)
    {
        stop.Token.ThrowIfCancellationRequested();
        typeof(GameSimBench).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sim, new object?[] { 0d, 0d });
        sim.OracleSetFrames(frame + 1);
    }
    stop.Token.ThrowIfCancellationRequested();
    var entities = Enumerable.Range(0, 256).Select(i => new Entity(i, Get<float>("_x")[i], Get<float>("_y")[i], Get<float>("_vx")[i], Get<float>("_vy")[i], Get<float>("_hp")[i], Get<float>("_dmg")[i], Get<byte>("_team")[i])).ToArray();
    int visible = sim.OracleVisible;
    var draw = Get<int>("_drawList"); var keys = Get<float>("_drawKey");
    uint rng = (uint)Field("_rng").GetValue(sim)!;
    using var bytes = new MemoryStream();
    using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(256); writer.Write(steps); writer.Write(rng); writer.Write(visible);
        foreach (var e in entities) { writer.Write(e.Id); writer.Write(e.X); writer.Write(e.Y); writer.Write(e.Vx); writer.Write(e.Vy); writer.Write(e.Health); writer.Write(e.Damage); writer.Write(e.Team); }
        for (int i = 0; i < visible; i++) { writer.Write(draw[i]); writer.Write(keys[i]); }
    }
    cases.Add(new { threads, scenario, steps, rngState = rng, visibleCount = visible,
        stateSha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), samples = entities.Take(8).ToArray() });
    FieldInfo Field(string name) => typeof(GameSimBench).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    T[] Get<T>(string name) => (T[])Field(name).GetValue(sim)!;
    void Call(string name) => typeof(GameSimBench).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sim, null);
    void Set(int id, float x, float y, float vx, float vy, float hp)
    { Get<float>("_x")[id] = x; Get<float>("_y")[id] = y; Get<float>("_vx")[id] = vx; Get<float>("_vy")[id] = vy; Get<float>("_hp")[id] = hp; }
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { referenceCommit = "ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6", source = "GameSimBench.cs MIT GustavoReset", runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    entities = 256, world = 1000, gridSize = 64, deltaTime = 1 / 60f, seed = 12345, warmupSteps = 0, hashFormat = "silex-gamesim-state-le-v1", cases }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
Console.WriteLine("PASS bounded original GameSim oracle: 256 entities; fixed 0/1/16 steps; 1/2/4 workers; no Run/warmup/FPS.");
record Entity(int Id, float X, float Y, float Vx, float Vy, float Health, float Damage, byte Team);
