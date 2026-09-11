using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WarDogs;
using WarDogs.Multiplayer;

if (args.Length >= 4 && args[0] == "--load") { await LoadTest.RunAsync(new Uri(args[1]), int.Parse(args[2]), args[3]); return; }
if (args.Length == 2 && args[0] == "--restart") { await RestartTest.RunAsync(args[1]); return; }
if (args.Length == 2 && args[0] == "--bots") { await BotsTest.RunAsync(new Uri(args[1])); return; }
if (args.Length > 0 && args[0].StartsWith("--")) throw new ArgumentException("Usage: --load ws://localhost:port/ws serverPid output.json");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
var capture = new PublishCapture();
capture.Toggle(10); Check(capture.Consume(10, new(80, 70)) == null && capture.Waiting, "publish ignores preexisting clipboard");
Check(capture.Consume(11, null) == null && capture.Waiting, "invalid clipboard keeps waiting");
Check(capture.Consume(12, new(80, 70)) != null && !capture.Waiting, "new coordinate consumed once");
Check(capture.Consume(13, new(81, 70)) == null, "publish is one-shot");
capture.Toggle(20); capture.Toggle(20); Check(!capture.Waiting, "second press cancels");
Check(!new NetworkPoint("bakurani", 80, 70).Same(new("ozeti", 80, 70)), "map participates in coordinate match");
Check(new NetworkPoint("bakurani", 80.0000001, 70).Same(new("bakurani", 80.0000002, 70)), "six-place numeric matching");
Check(!new NetworkPoint("bakurani", double.NaN, 0).Valid, "reject nonfinite coordinate");
var temp = Path.Combine(Path.GetTempPath(), "wardogs-identity-" + Guid.NewGuid().ToString("N"));
try
{
    var uid = Identity.LoadOrCreate(temp); Check(Guid.TryParse(uid, out _), "random UUID");
    Check(uid == Identity.LoadOrCreate(temp), "UID survives restart");
    File.WriteAllText(Path.Combine(temp, "identity.json"), "{\"uid\":\"bad\"}");
    var rejected = false; try { Identity.LoadOrCreate(temp); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "invalid identity is not silently replaced");
}
finally { File.Delete(Path.Combine(temp, "identity.json")); Directory.Delete(temp); }
var state = new RoomState(); var received = 0; state.TaskReceived += (_, _) => received++;
var member = new RoomMember { Uid = "A", SessionId = "old", Tasks = new() { new() { Id = "T", Point = new("bakurani", 80, 70) } } };
state.Apply(new() { V = Protocol.Version, Type = "snapshot", RoomId = "R", Revision = 1, Members = new() { member } });
state.Apply(new() { V = Protocol.Version, Type = "member", RoomId = "R", Revision = 2, Member = member });
Check(received == 1, "snapshot/member deduplicate history");
Check(!state.Apply(new() { V = Protocol.Version, Type = "removed", RoomId = "old-room", Revision = 100, Uid = "A" }), "old room event ignored");
Check(!state.Apply(new() { V = Protocol.Version, Type = "removed", RoomId = "R", Revision = 1, Uid = "A" }), "stale revision ignored");
state.Apply(new() { V = Protocol.Version, Type = "member", RoomId = "R", Revision = 3, Member = new() { Uid = "A", SessionId = "new" } });
Check(state.Members["A"].Tasks.Count == 0 && state.Members["A"].SessionId == "new", "same UID replacement clears old tasks");
state.Apply(new() { V = Protocol.Version, Type = "identity", RoomId = "R", Revision = 4, Identity = new("A", "Latest Name") });
Check(state.ResolveName("A") == "Latest Name", "identity event updates latest display name");

if (args.Length > 0)
{
    var uri = new Uri(args[0]);
    await using var a = new RoomClient(); await using var b = new RoomClient();
    var aEvents = new ConcurrentQueue<RoomEvent>(); var bEvents = new ConcurrentQueue<RoomEvent>();
    a.Received += aEvents.Enqueue; b.Received += bEvents.Enqueue;
    var room = "test-" + Guid.NewGuid().ToString("N")[..12];
    var uidA = Guid.NewGuid().ToString("D");
    RoomMessage Join(string uid) => new() { Type = "join", Room = room, Uid = uid, Callsign = "Same", Role = "gunner", Map = "bakurani", Weapon = "mortar" };
    async Task Until(Func<bool> condition, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("Timeout: " + label); await Task.Delay(30); }
        Check(true, label);
    }
    var uidB=Guid.NewGuid().ToString("D");await a.StartAsync(uri, Join(uidA)); await b.StartAsync(uri, Join(uidB));
    await Until(() => a.Connected && b.Connected, "C# clients join real Go server");
    await Until(() => bEvents.Any(e => e.Member?.DisplayName == "Same#2") || bEvents.Any(e => e.Members.Any(m => m.DisplayName == "Same#2")), "duplicate callsign labels transmitted");
    var taskId = Guid.NewGuid().ToString("D"); var point = new NetworkPoint("bakurani", 80, 70);
    await a.SendAsync(new() { Type = "publish", TaskId = taskId, Point = point });
    await Until(() => bEvents.Any(e => e.Member?.Tasks.Any(t => t.Id == taskId) == true), "task broadcast and ACK across languages");
    await a.SendAsync(new() { Type = "publish", TaskId = taskId, Point = point });
    Check(bEvents.Where(e => e.Member?.Uid == uidA).Last().Member!.Tasks.Count == 1, "repeated task ID does not duplicate");
    await b.SendAsync(new() { Type = "target", Point = point, Solved = true });
    await Until(() => aEvents.Any(e => e.Member is { } m&&m.Uid==uidB&&m.Solved&&m.Target?.Same(point)==true), "successful solver state relayed for client attribution");
    await a.SendAsync(new() { Type = "profile", Callsign = "Renamed", Role = "gunner", Map = "bakurani", Weapon = "mortar" });
    await Until(() => bEvents.Any(e => e.Identity?.Uid == uidA && e.Identity.Name == "Renamed"), "latest identity update crosses protocol boundary");
    var snapshotCount = bEvents.Count(e => e.Type == "snapshot");
    var originalSession = bEvents.First(e => e.Type == "snapshot").SessionId;
    await b.SendAsync(new() { Type = "sync" });
    await Until(() => bEvents.Count(e => e.Type == "snapshot") > snapshotCount, "sync returns a fresh snapshot");
    Check(bEvents.Last(e => e.Type == "snapshot").SessionId == originalSession, "sync preserves session identity");
    await using var replacement = new RoomClient(); var replacementEvents = new ConcurrentQueue<RoomEvent>(); replacement.Received += replacementEvents.Enqueue;
    var replaced = false; a.StatusChanged += text => { if (text.Contains("停止重连")) replaced = true; };
    await replacement.StartAsync(uri, Join(uidA));
    await Until(() => replacement.Connected && replaced, "same UID evicts old client without reconnect loop");
    Check(replacementEvents.First(e => e.Type == "snapshot").Members.Single(m => m.Uid == uidA).Tasks.Count == 0, "replacement snapshot has no old own tasks");
    await replacement.StopAsync();
    await Until(() => bEvents.Any(e => e.Member?.Uid == uidA && !e.Member.Online), "disconnect becomes offline member");
    Console.WriteLine("Real Go/C# integration passed.");
}
Console.WriteLine("Multiplayer tests passed.");

static class BotsTest
{
    const string ScoutBot = "7b0d9f6c-6cc4-4bc3-9f4c-6cfffa9b83f1";
    const string GunnerBot = "69d97a48-58dc-4ef0-af38-d6de2481e7a6";
    public static async Task RunAsync(Uri uri)
    {
        await using var gunner = new RoomClient(); await using var scout = new RoomClient();
        var members = new ConcurrentDictionary<string, RoomMember>();
        void Observe(RoomEvent e)
        {
            if (e.Type == "snapshot") foreach (var member in e.Members) members[member.Uid] = member;
            else if (e.Type == "member" && e.Member != null) members[e.Member.Uid] = e.Member;
            else if (e.Type == "removed" && e.Uid != null) members.TryRemove(e.Uid, out _);
        }
        gunner.Received += Observe; scout.Received += Observe;
        RoomMessage Join(string role) => new() { Type = "join", Uid = Guid.NewGuid().ToString("D"), Callsign = "E2E " + role, Room = "testzz4z", Role = role, Map = "bakurani", Weapon = "mortar" };
        async Task Until(Func<bool> predicate, string label)
        {
            var watch = Stopwatch.StartNew();
            while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("Timeout: " + label); await Task.Delay(25); }
            Console.WriteLine("PASS " + label);
        }
        await gunner.StartAsync(uri, Join("gunner")); await scout.StartAsync(uri, Join("scout"));
        await Until(() => members.ContainsKey(ScoutBot) && members.ContainsKey(GunnerBot), "both fixed-identity bots are online");
        var origin = new NetworkPoint("bakurani", 70, 60); var target = new NetworkPoint("bakurani", 71, 61);
        await gunner.SendAsync(new() { Type = "origin", Point = origin });
        await gunner.SendAsync(new() { Type = "target", Point = target });
        var response = new NetworkPoint("bakurani", 72, 62);
        await Until(() => members.TryGetValue(ScoutBot, out var bot) && bot.Tasks.Any(t => t.Point.Same(response)), "scout bot publishes target plus one");
        var beacon = new NetworkPoint("bakurani", 80, 70); var beaconId = Guid.NewGuid().ToString("D");
        await scout.SendAsync(new() { Type = "publish", TaskId = beaconId, Point = beacon });
        await Until(() => members.TryGetValue(GunnerBot, out var bot) && bot.Origin?.Same(new("bakurani", 82, 72)) == true, "gunner bot sets beacon plus two origin");
        await Until(() => members.TryGetValue(GunnerBot,out var bot)&&bot.Solved&&bot.Target?.Same(beacon)==true, "gunner bot reports simulated solved state");
        await Task.Delay(1500);
        var replies = members.TryGetValue(ScoutBot, out var scoutBot) ? scoutBot.Tasks.Count(t => t.Point.Same(response)) : 0;
        if (replies != 1) throw new Exception("bot event loop or duplicate response detected: " + replies);
        Console.WriteLine("PASS bot events do not loop or duplicate current target");
        Console.WriteLine("Test-room bot integration passed (simulated solved, not ballistics validation).");
    }
}
