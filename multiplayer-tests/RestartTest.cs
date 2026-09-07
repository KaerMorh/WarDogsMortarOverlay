using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WarDogs.Multiplayer;

internal static class RestartTest
{
    public static async Task RunAsync(string serverPath)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(Path.GetFullPath(serverPath)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.Environment["WARDOGS_ADDR"] = "127.0.0.1:" + port;
        Process? server = Process.Start(start) ?? throw new Exception("Could not start test server");
        await using var client = new RoomClient();
        var snapshots = new ConcurrentQueue<RoomEvent>(); client.Received += e => { if (e.Type == "snapshot") snapshots.Enqueue(e); };
        async Task Until(Func<bool> condition, string label)
        {
            var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new Exception("Timeout: " + label); await Task.Delay(30); } Console.WriteLine("PASS " + label);
        }
        try
        {
            await client.StartAsync(new("ws://127.0.0.1:" + port + "/ws"), new() { Type = "join", Uid = Guid.NewGuid().ToString("D"), Room = "restart-test", Callsign = "Restart", Role = "gunner", Map = "bakurani" });
            await Until(() => client.Connected && snapshots.Count == 1, "initial server connection");
            await client.SendAsync(new() { Type = "publish", TaskId = Guid.NewGuid().ToString("D"), Point = new("bakurani", 80, 70) });
            server.Kill(); await server.WaitForExitAsync(); server.Dispose(); server = null;
            await Until(() => !client.Connected, "server loss detected");
            var rejected = false; try { await client.SendAsync(new() { Type = "publish", TaskId = Guid.NewGuid().ToString("D"), Point = new("bakurani", 81, 70) }); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Offline publish was queued"); Console.WriteLine("PASS offline publish rejected rather than replayed");
            server = Process.Start(start) ?? throw new Exception("Could not restart test server");
            await Until(() => client.Connected && snapshots.Count >= 2, "automatic reconnect after server restart");
            var first = snapshots.First(); var latest = snapshots.Last();
            if (first.RoomId == latest.RoomId || first.SessionId == latest.SessionId || latest.Members.Any(m => m.Tasks.Count != 0)) throw new Exception("Restart restored stale room or tasks");
            Console.WriteLine("PASS recreated room is empty with new room and session IDs");
        }
        finally
        {
            await client.StopAsync();
            if (server != null) { if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); } server.Dispose(); }
        }
    }
}
