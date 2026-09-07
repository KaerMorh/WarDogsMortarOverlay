using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WarDogs.Multiplayer;

internal static class LoadTest
{
    public static async Task RunAsync(Uri uri, int processId, string output)
    {
        var results = new List<object>();
        using var server = Process.GetProcessById(processId);
        foreach (var count in new[] { 10, 50, 100, 32 })
        {
            var clients = new List<RoomClient>();
            var token = Guid.NewGuid().ToString("N")[..8];
            long events = 0;
            var times = new ConcurrentBag<double>();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var client = new RoomClient(); clients.Add(client); client.Received += _ => Interlocked.Increment(ref events);
                    await client.StartAsync(uri, new() { Type = "join", Uid = Guid.NewGuid().ToString("D"), Room = "load-" + token + "-" + (count == 32 ? 0 : i / 10), Callsign = "Load" + i, Role = "gunner", Map = "bakurani" });
                    // Stagger joins: test steady state, not an artificial synchronized reconnect storm.
                    await Task.Delay(15);
                }
                var deadline = Stopwatch.StartNew(); while (clients.Any(c => !c.Connected)) { if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Load join failed"); await Task.Delay(25); }
                foreach (var (client, i) in clients.Select((client, i) => (client, i)))
                    for (var task = 0; task < 3; task++) await client.SendAsync(new() { Type = "publish", TaskId = Guid.NewGuid().ToString("D"), Point = new("bakurani", 80 + i * .01, 70 + task * .001) });
                await Task.Delay(500);
                server.Refresh(); var beforeCpu = server.TotalProcessorTime; var peak = server.WorkingSet64; var started = Stopwatch.StartNew();
                using var sampleCancellation = new CancellationTokenSource();
                var sampler = Task.Run(async () => { while (!sampleCancellation.IsCancellationRequested) { server.Refresh(); peak = Math.Max(peak, server.WorkingSet64); try { await Task.Delay(100, sampleCancellation.Token); } catch (OperationCanceledException) { break; } } });
                try
                {
                    await Task.WhenAll(clients.Select(async (client, i) =>
                    {
                        for (var round = 0; round < 30; round++)
                        {
                            var watch = Stopwatch.StartNew();
                            await client.SendAsync(new() { Type = "target", Point = new("bakurani", 80 + i * .01, 70 + round * .001), Solved = true });
                            times.Add(watch.Elapsed.TotalMilliseconds);
                            await Task.Delay(500); // Each client updates twice a second for ~15s.
                        }
                    }));
                }
                finally { sampleCancellation.Cancel(); await sampler; }
                started.Stop(); server.Refresh();
                var sorted = times.Order().ToArray();
                var result = new { clients = count, roomSize = count == 32 ? 32 : 10, durationSeconds = started.Elapsed.TotalSeconds, messages = times.Count, receivedEvents = events, peakWorkingSetMiB = peak / 1048576d, cpuOneCorePercent = (server.TotalProcessorTime - beforeCpu).TotalSeconds / started.Elapsed.TotalSeconds * 100, ackP50Ms = sorted[sorted.Length / 2], ackP95Ms = sorted[(int)(sorted.Length * .95)], ackMaxMs = sorted[^1], allConnected = clients.All(c => c.Connected) };
                results.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
                if (!result.allConnected) throw new Exception("Client disconnected under load");
            }
            finally { foreach (var client in clients) await client.DisposeAsync(); }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { environment = "Local Windows, loopback, Go server and C# clients share host; not Linux 2-core server capacity", results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
