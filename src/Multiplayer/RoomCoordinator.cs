using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WarDogs.Multiplayer;

// Desktop adapter. All mutable model/UI work is owned by the Dispatcher.
public sealed class RoomCoordinator
{
    readonly Controller controller;
    readonly Dispatcher dispatcher;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly SemaphoreSlim lifecycle = new(1, 1);
    RoomClient? client;
    CancellationTokenSource? session;
    Task? joining, syncRequest;
    string? uid;
    string? joinedRoom;
    string? activeKey;
    long generation, joiningGeneration;
    bool synchronizing;
    bool showOnJoin;
    bool joiningDisplay;
    DateTimeOffset lastConnectionAttempt = DateTimeOffset.MinValue, lastSyncRequest = DateTimeOffset.MinValue;
    string? lastProfile, lastOrigin, lastTarget;
    public RoomState State { get; } = new();
    public PublishCapture Capture { get; } = new();
    public string Status { get; private set; } = "未加入房间";
    public bool Connected => client?.Connected == true;
    public bool Active => client != null;
    public bool CanJoin => !joiningDisplay && (client == null || activeKey != RequestedKey);
    public bool CanLeave => joiningDisplay || client != null;
    public bool CanSync => Connected && syncRequest?.IsCompleted != false;
    public string JoinButtonText => joiningDisplay ? "加入中…" : client == null ? "加入 / 创建房间" : activeKey != RequestedKey ? "切换房间" : Connected ? "已加入" : "正在重连";
    public string? Uid => uid;
    public event Action? Changed;
    public RoomCoordinator(Controller controller)
    {
        this.controller = controller; dispatcher = Dispatcher.CurrentDispatcher;
        State.TaskReceived += controller.RecordRoomTask;
        timer.Tick += async (_, _) => await PushLocalStateAsync();
        timer.Start();
    }
    MultiplayerPreferences Pref => controller.Pref.Multiplayer;
    RoomMessage Profile(string type) => new() { Type = type, Uid = uid, Room = joinedRoom ?? Pref.Room.Trim().ToLowerInvariant(), Callsign = Pref.Callsign.Trim(), Role = Pref.Role, Map = controller.State.Map };
    string RequestedKey => Pref.ServerUrl.Trim() + "\n" + Pref.Room.Trim().ToLowerInvariant();
    public Task JoinAsync() => JoinAsync(false);
    internal Task JoinForVerificationAsync() => JoinAsync(true);
    Task JoinAsync(bool verify)
    {
        if (joining?.IsCompleted == false && joiningGeneration == generation) return joining;
        var serverUrl = Pref.ServerUrl.Trim();
        var room = Pref.Room.Trim().ToLowerInvariant();
        var callsign = Pref.Callsign.Trim();
        var role = Pref.Role;
        var map = controller.State.Map;
        var requestedKey = serverUrl + "\n" + room;
        if (client != null && activeKey == requestedKey) return Task.CompletedTask;
        var clickedAt = DateTimeOffset.UtcNow;
        Uri uri;
        try
        {
            if (controller.Demo && !verify) throw new ArgumentException("演示模式不连接房间");
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out uri!)) throw new ArgumentException("请输入有效服务地址。");
            if (uri.Scheme is not ("ws" or "wss") || uri.Scheme == "ws" && !uri.IsLoopback) throw new ArgumentException("公网服务使用 wss://，本机调试可用 ws://。");
            if (verify && (!controller.Demo || !uri.IsLoopback)) throw new ArgumentException("验证连接仅允许演示模式访问本机。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(room, "^[a-z0-9_-]{4,32}$")) throw new ArgumentException("房间码为 4–32 位字母、数字、_ 或 -。");
            if (string.IsNullOrWhiteSpace(callsign) || callsign.EnumerateRunes().Count() > 32) throw new ArgumentException("Callsign 需为 1–32 个字符。");
            uid ??= controller.Demo ? Guid.NewGuid().ToString("D") : Identity.LoadOrCreate(Controller.UserDir);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            joiningDisplay = true; Status = "加入中…"; Changed?.Invoke();
            joiningGeneration = generation;
            return joining = ShowJoinFailureAsync(ex, generation, clickedAt);
        }
        session?.Cancel();
        session = new CancellationTokenSource();
        joiningDisplay = true; Status = "加入中…"; Changed?.Invoke();
        joiningGeneration = ++generation;
        return joining = JoinCoreAsync(joiningGeneration, session, requestedKey, uri, room, callsign, role, map, clickedAt);
    }
    async Task ShowJoinFailureAsync(Exception error, long currentGeneration, DateTimeOffset clickedAt)
    {
        var hold = TimeSpan.FromSeconds(3) - (DateTimeOffset.UtcNow - clickedAt);
        if (hold > TimeSpan.Zero) await Task.Delay(hold);
        if (generation != currentGeneration) return;
        joiningDisplay = false; Status = error.Message; controller.Notify(Status); Changed?.Invoke();
    }
    async Task JoinCoreAsync(long currentGeneration, CancellationTokenSource intent, string requestedKey, Uri uri, string room, string callsign, string role, string map, DateTimeOffset clickedAt)
    {
        var entered = false;
        try
        {
            await lifecycle.WaitAsync(intent.Token); entered = true;
            var old = client; client = null;
            if (old != null) await old.DisposeAsync();
            var throttle = TimeSpan.FromSeconds(2) - (DateTimeOffset.UtcNow - lastConnectionAttempt);
            if (throttle > TimeSpan.Zero) await Task.Delay(throttle, intent.Token);
            intent.Token.ThrowIfCancellationRequested();
            joinedRoom = room;
            activeKey = requestedKey;
            showOnJoin = true;
            var next = new RoomClient(); client = next;
            next.StatusChanged += text => dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != currentGeneration || controller.IsClosing) return;
                Status = joiningDisplay ? "加入中…" : text;
                if (!Connected) Capture.Cancel();
                Changed?.Invoke();
            }));
            next.Received += e => dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != currentGeneration || controller.IsClosing) return;
                if (State.Apply(e))
                {
                    if (e.Type == "snapshot")
                    {
                        lastProfile = lastOrigin = lastTarget = null;
                        if(showOnJoin&&!joiningDisplay){showOnJoin=false;controller.ShowRoomTasks();}
                        if (State.Map != controller.State.Map) controller.Notify("房间地图与本地不同 · 选择任务时请确认地图");
                    }
                    controller.UpdateRoomHistory(); Changed?.Invoke();
                }
            }));
            next.Ended += () => dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != currentGeneration || !ReferenceEquals(client, next) || joiningDisplay) return;
                client = null; activeKey = null; Changed?.Invoke();
            }));
            var remaining = TimeSpan.FromSeconds(10) - (DateTimeOffset.UtcNow - clickedAt);
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(intent.Token);
            deadline.CancelAfter(remaining);
            lastConnectionAttempt = DateTimeOffset.UtcNow;
            var join = new RoomMessage { Type = "join", Uid = uid, Room = room, Callsign = callsign, Role = role, Map = map };
            try { await next.StartAsync(uri, join, deadline.Token); }
            catch (OperationCanceledException) when (!intent.IsCancellationRequested) { throw new TimeoutException("连接超时 · 请检查服务地址和网络"); }
            await Task.Delay(TimeSpan.FromSeconds(1), intent.Token);
            if (generation != currentGeneration) return;
            joiningDisplay = false;
            Status = next.Connected ? "已加入房间 " + joinedRoom : "正在重连";
            if (next.Connected && showOnJoin) { showOnJoin = false; controller.ShowRoomTasks(); }
            controller.Save();
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (intent.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException or TimeoutException)
        {
            var failed = activeKey == requestedKey ? client : null;
            if (failed != null) { client = null; activeKey = null; }
            if (failed != null) await failed.DisposeAsync();
            var hold = TimeSpan.FromSeconds(3) - (DateTimeOffset.UtcNow - clickedAt);
            if (hold > TimeSpan.Zero) try { await Task.Delay(hold, intent.Token); } catch (OperationCanceledException) { }
            if (generation == currentGeneration && !intent.IsCancellationRequested)
            {
                joiningDisplay = false; Status = ex is TimeoutException && string.IsNullOrEmpty(ex.Message) ? "连接超时 · 请检查服务地址和网络" : ex.Message;
                controller.Notify(Status); Changed?.Invoke();
            }
        }
        finally { if (entered) lifecycle.Release(); }
    }
    public async Task LeaveAsync()
    {
        var currentGeneration = ++generation;
        session?.Cancel(); session = null;
        var old = client; client = null; activeKey = null; joiningDisplay = false;
        Capture.Cancel(); State.Clear(); Status = "未加入房间";
        joinedRoom = null;
        lastProfile = lastOrigin = lastTarget = null;
        Changed?.Invoke();
        await lifecycle.WaitAsync();
        try { if (old != null) await old.DisposeAsync(); }
        finally { lifecycle.Release(); }
        if (generation == currentGeneration) Changed?.Invoke();
    }
    public void PreferencesChanged()
    {
        lastProfile = null;
        if (Pref.ShareTarget) lastTarget = null;
        if (client != null) client.ReconnectProfile = Profile("join");
        controller.Save(); Changed?.Invoke();
    }
    public void TogglePublish(uint sequence)
    {
        if (!Connected) { controller.Notify("先加入房间，再发布任务"); return; }
        Capture.Toggle(sequence);
        controller.Notify(Capture.Waiting ? "发布任务 · 等待下一次复制有效坐标，再按取消" : "已取消任务发布等待");
        Changed?.Invoke();
    }
    public async Task PublishAsync(Coord coordinate)
    {
        var point = new NetworkPoint(controller.State.Map, coordinate.X, coordinate.Y);
        if (!point.Valid) { controller.Notify("坐标超出地图范围 · 未发布"); return; }
        var active = client;
        if (active == null || !active.Connected) { controller.Notify("连接已断开 · 任务未发布，请重新操作"); return; }
        try { await active.SendAsync(new() { Type = "publish", TaskId = Guid.NewGuid().ToString("D"), Point = point }); controller.Notify("任务已发布"); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Net.WebSockets.WebSocketException) { controller.Notify("任务发送未确认 · 请检查房间后重新发布"); }
        Changed?.Invoke();
    }
    async Task PushLocalStateAsync()
    {
        var active = client;
        if (controller.IsClosing || synchronizing || active?.Connected != true || State.SessionId.Length == 0) return;
        if (active != null) active.ReconnectProfile = Profile("join");
        RoomMessage? message = null; Action? acknowledge = null;
        var profile = Profile("profile");
        var profileKey = System.Text.Json.JsonSerializer.Serialize(profile, Protocol.Json);
        var origin = controller.State.Current.Origin;
        var originPoint = origin == null ? null : new NetworkPoint(controller.State.Map, origin.X, origin.Y);
        if (originPoint?.Valid == false) originPoint = null;
        var originKey = System.Text.Json.JsonSerializer.Serialize(originPoint, Protocol.Json);
        var target = controller.State.Current.Target;
        var targetPoint = target == null ? null : new NetworkPoint(controller.State.Map, target.X, target.Y);
        if (targetPoint?.Valid == false) targetPoint = null;
        var solved = Pref.Role == "gunner" && targetPoint != null && controller.Result is { InRange: true } result && (result.Single != null || result.Low != null || result.High != null);
        var targetKey = System.Text.Json.JsonSerializer.Serialize(new { point = targetPoint, solved }, Protocol.Json);
        if (lastProfile != profileKey) { message = profile; acknowledge = () => lastProfile = profileKey; }
        else if (lastOrigin != originKey) { message = new() { Type = "origin", Point = originPoint }; acknowledge = () => lastOrigin = originKey; }
        else if (Pref.ShareTarget && lastTarget != targetKey) { message = new() { Type = "target", Point = targetPoint, Solved = solved }; acknowledge = () => lastTarget = targetKey; }
        if (message == null) return;
        synchronizing = true;
        var currentGeneration = generation;
        try { await active!.SendAsync(message); if (generation == currentGeneration) acknowledge!(); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Net.WebSockets.WebSocketException)
        { if (generation == currentGeneration) { Status = "状态同步未确认 · 正在重试"; Changed?.Invoke(); } }
        finally { synchronizing = false; }
    }
    public Task RequestSyncAsync()
    {
        if (syncRequest?.IsCompleted == false) return syncRequest;
        return syncRequest = RequestSyncCoreAsync(generation, session?.Token ?? CancellationToken.None);
    }
    async Task RequestSyncCoreAsync(long currentGeneration, CancellationToken token)
    {
        var active = client;
        if (active?.Connected != true) { controller.Notify("尚未加入房间，无法同步"); return; }
        try
        {
            var delay = TimeSpan.FromSeconds(2) - (DateTimeOffset.UtcNow - lastSyncRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            if (generation != currentGeneration || !ReferenceEquals(client, active)) return;
            lastSyncRequest = DateTimeOffset.UtcNow;
            await active.SendAsync(new() { Type = "sync" }, token);
            if (generation == currentGeneration) { Status = "房间状态已同步"; Changed?.Invoke(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or System.Net.WebSockets.WebSocketException)
        { if (generation == currentGeneration) { Status = "房间状态同步失败 · 请稍后重试"; Changed?.Invoke(); } }
    }
    public bool Select(NetworkPoint point, string source)
    {
        if (!point.Valid) { controller.Notify("任务坐标无效"); return false; }
        if (controller.State.Map != point.Map)
        {
            var answer = MessageBox.Show($"任务位于 {(point.Map == "bakurani" ? "Bakurani" : "Ozeti")}，切换地图并解算？", "任务地图不同", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return false;
            controller.State.ChangeMap();
        }
        controller.CancelInputForRoomSelection();
        controller.State.SetTarget(point.Coordinate, source);
        return true;
    }
    public async Task ShutdownAsync() { timer.Stop(); await LeaveAsync(); }
}
