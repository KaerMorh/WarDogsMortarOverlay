using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;

namespace WarDogs.Multiplayer;

// No WPF dependency: the desktop adapter marshals callbacks to its Dispatcher.
public sealed class RoomClient : IAsyncDisposable
{
    readonly SemaphoreSlim writes = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource> pending = new();
    CancellationTokenSource? lifetime;
    Task? runner;
    ClientWebSocket? socket;
    volatile bool connected;
    public bool Connected => connected;
    public RoomMessage? ReconnectProfile { get; set; }
    public event Action<RoomEvent>? Received;
    public event Action<string>? StatusChanged;
    public event Action? Ended;
    public async Task StartAsync(Uri uri, RoomMessage join, CancellationToken cancellationToken = default)
    {
        if (uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("服务地址必须以 ws:// 或 wss:// 开头。");
        if (uri.Scheme == "ws" && !uri.IsLoopback) throw new ArgumentException("公网服务请使用 wss://；ws:// 仅供本机开发。");
        await StopAsync();
        ReconnectProfile = join;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var initialJoin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner = RunAsync(uri, join, initialJoin, lifetime.Token);
        await initialJoin.Task;
    }
    async Task RunAsync(Uri uri, RoomMessage join, TaskCompletionSource initialJoin, CancellationToken token)
    {
        var attempt = 0;
        var terminal = false;
        while (!token.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            ws.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
            socket = ws;
            try
            {
                StatusChanged?.Invoke(attempt == 0 ? "连接中" : "重新连接中");
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                { deadline.CancelAfter(TimeSpan.FromSeconds(10)); await ws.ConnectAsync(uri, deadline.Token); await WriteAsync(ws, ReconnectProfile ?? join, deadline.Token); }
                using var joinDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                joinDeadline.CancelAfter(TimeSpan.FromSeconds(12));
                while (!token.IsCancellationRequested)
                {
                    var e = await ReadAsync(ws, connected ? token : joinDeadline.Token);
                    if (e == null)
                    {
                        if ((int?)ws.CloseStatus == 4001)
                        {
                            var error = new IOException("身份已在另一连接进入房间 · 已停止重连");
                            terminal = true; StatusChanged?.Invoke(error.Message); initialJoin.TrySetException(error); break;
                        }
                        throw new IOException("连接已关闭");
                    }
                    if (e.V != Protocol.Version)
                    {
                        var error = new IOException("服务端协议版本不兼容");
                        StatusChanged?.Invoke(error.Message); initialJoin.TrySetException(error); terminal = true; break;
                    }
                    if (e.Type == "error")
                    {
                        if (e.RequestId != null && pending.TryRemove(e.RequestId, out var rejected)) rejected.TrySetException(new IOException(ErrorText(e.Code)));
                        if (e.Code == "session_replaced" || !connected)
                        {
                            var error = new IOException(ErrorText(e.Code));
                            StatusChanged?.Invoke(error.Message); initialJoin.TrySetException(error); terminal = true; break;
                        }
                    }
                    if (e.Type == "ack" && e.RequestId != null && pending.TryRemove(e.RequestId, out var ack)) ack.TrySetResult();
                    if (e.Type == "snapshot") { connected = true; attempt = 0; initialJoin.TrySetResult(); StatusChanged?.Invoke("已加入房间 " + e.Room); }
                    Received?.Invoke(e);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or JsonException)
            {
                if (token.IsCancellationRequested) break;
                StatusChanged?.Invoke("连接中断 · 将自动重试");
            }
            finally
            {
                connected = false;
                if (ReferenceEquals(socket, ws)) socket = null;
                foreach (var item in pending) if (pending.TryRemove(item.Key, out var tcs)) tcs.TrySetException(new IOException("连接中断，操作未确认"));
            }
            if (terminal) break;
            attempt++;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)))) + TimeSpan.FromMilliseconds(Random.Shared.Next(250)), token); }
            catch (OperationCanceledException) { break; }
        }
        if (token.IsCancellationRequested) initialJoin.TrySetCanceled(token);
        else initialJoin.TrySetException(new IOException("连接已关闭"));
        Ended?.Invoke();
    }
    public async Task SendAsync(RoomMessage message, CancellationToken cancellationToken = default)
    {
        var ws = socket;
        if (!connected || ws == null) throw new IOException("尚未加入房间");
        if (pending.Count >= 32) throw new IOException("发送队列已满，请稍后重试");
        message.RequestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[message.RequestId] = tcs;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try { await WriteAsync(ws, message, deadline.Token); await tcs.Task.WaitAsync(deadline.Token); }
        catch (ObjectDisposedException ex) { throw new IOException("连接已结束，操作未确认", ex); }
        finally { pending.TryRemove(message.RequestId, out _); }
    }
    async Task WriteAsync(ClientWebSocket ws, RoomMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);
        if (bytes.Length > 8192) throw new IOException("消息过大");
        await writes.WaitAsync(token);
        try { await ws.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token); }
        finally { writes.Release(); }
    }
    static async Task<RoomEvent?> ReadAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var output = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text || output.Length + result.Count > 1024 * 1024) throw new IOException("服务端消息无效或过大");
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<RoomEvent>(output.ToArray(), Protocol.Json) ?? throw new JsonException("空消息");
    }
    public async Task StopAsync()
    {
        lifetime?.Cancel(); socket?.Abort();
        if (runner != null) { try { await runner; } catch (OperationCanceledException) { } }
        runner = null; lifetime?.Dispose(); lifetime = null; connected = false;
    }
    // In-flight sends can still unwind after the receive loop stops. This
    // managed semaphore has no WaitHandle and must remain usable for Release.
    public async ValueTask DisposeAsync() { await StopAsync(); }
    public static string ErrorText(string? code) => code switch
    {
        "session_replaced" => "身份已在另一连接进入房间 · 已停止重连",
        "room_full" => "房间人数已满", "server_full" => "服务器房间已满",
        "invalid_message" => "服务端拒绝了无效数据", _ => "联机操作失败"
    };
}
