using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WarDogs;
using WarDogs.Multiplayer;

if (args.Length >= 4 && args[0] == "--load") { await LoadTest.RunAsync(new Uri(args[1]), int.Parse(args[2]), args[3]); return; }
if (args.Length == 2 && args[0] == "--restart") { await RestartTest.RunAsync(args[1]); return; }
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
state.Apply(new() { V = 1, Type = "snapshot", RoomId = "R", Revision = 1, Members = new() { member } });
state.Apply(new() { V = 1, Type = "member", RoomId = "R", Revision = 2, Member = member });
Check(received == 1, "snapshot/member deduplicate history");
Check(!state.Apply(new() { V = 1, Type = "removed", RoomId = "old-room", Revision = 100, Uid = "A" }), "old room event ignored");
Check(!state.Apply(new() { V = 1, Type = "removed", RoomId = "R", Revision = 1, Uid = "A" }), "stale revision ignored");
state.Apply(new() { V = 1, Type = "member", RoomId = "R", Revision = 3, Member = new() { Uid = "A", SessionId = "new" } });
Check(state.Members["A"].Tasks.Count == 0 && state.Members["A"].SessionId == "new", "same UID replacement clears old tasks");

if (args.Length > 0)
{
    var uri = new Uri(args[0]);
    await using var a = new RoomClient(); await using var b = new RoomClient();
    var aEvents = new ConcurrentQueue<RoomEvent>(); var bEvents = new ConcurrentQueue<RoomEvent>();
    a.Received += aEvents.Enqueue; b.Received += bEvents.Enqueue;
    var room = "test-" + Guid.NewGuid().ToString("N")[..12];
    var uidA = Guid.NewGuid().ToString("D");
    RoomMessage Join(string uid) => new() { Type = "join", Room = room, Uid = uid, Callsign = "Same", Role = "gunner", Map = "bakurani" };
    async Task Until(Func<bool> condition, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("Timeout: " + label); await Task.Delay(30); }
        Check(true, label);
    }
    await a.StartAsync(uri, Join(uidA)); await b.StartAsync(uri, Join(Guid.NewGuid().ToString("D")));
    await Until(() => a.Connected && b.Connected, "C# clients join real Go server");
    await Until(() => bEvents.Any(e => e.Member?.DisplayName == "Same#2") || bEvents.Any(e => e.Members.Any(m => m.DisplayName == "Same#2")), "duplicate callsign labels transmitted");
    var taskId = Guid.NewGuid().ToString("D"); var point = new NetworkPoint("bakurani", 80, 70);
    await a.SendAsync(new() { Type = "publish", TaskId = taskId, Point = point });
    await Until(() => bEvents.Any(e => e.Member?.Tasks.Any(t => t.Id == taskId) == true), "task broadcast and ACK across languages");
    await a.SendAsync(new() { Type = "publish", TaskId = taskId, Point = point });
    Check(bEvents.Where(e => e.Member?.Uid == uidA).Last().Member!.Tasks.Count == 1, "repeated task ID does not duplicate");
    await b.SendAsync(new() { Type = "target", Point = point, Solved = true });
    await Until(() => aEvents.Any(e => e.Member?.Tasks.Any(t => t.Id == taskId && t.SolvedBy.Count == 1) == true), "successful solver attribution");
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
