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
    RoomClient? client;
    string? uid;
    string? joinedRoom;
    long generation;
    bool synchronizing;
    bool showOnJoin;
    string? lastProfile, lastOrigin, lastTarget;
    public RoomState State { get; } = new();
    public PublishCapture Capture { get; } = new();
    public string Status { get; private set; } = "未加入房间";
    public bool Connected => client?.Connected == true;
    public bool Active => client != null;
    public string? Uid => uid;
    public event Action? Changed;
    public RoomCoordinator(Controller controller)
    {
        this.controller = controller; dispatcher = Dispatcher.CurrentDispatcher;
        State.TaskReceived += controller.RecordRoomTask;
        timer.Tick += async (_, _) => await SynchronizeAsync();
        timer.Start();
    }
    MultiplayerPreferences Pref => controller.Pref.Multiplayer;
    RoomMessage Profile(string type) => new() { Type = type, Uid = uid, Room = joinedRoom ?? Pref.Room.Trim().ToLowerInvariant(), Callsign = Pref.Callsign.Trim(), Role = Pref.Role, Map = controller.State.Map };
    public Task JoinAsync() => JoinCoreAsync(false);
    internal Task JoinForVerificationAsync() => JoinCoreAsync(true);
    async Task JoinCoreAsync(bool verify)
    {
        if (controller.Demo && !verify) { controller.Notify("演示模式不连接房间"); return; }
        try
        {
            if (!Uri.TryCreate(Pref.ServerUrl.Trim(), UriKind.Absolute, out var uri)) throw new ArgumentException("请输入有效服务地址。");
            if (uri.Scheme is not ("ws" or "wss") || uri.Scheme == "ws" && !uri.IsLoopback) throw new ArgumentException("公网服务使用 wss://，本机调试可用 ws://。");
            if (verify && (!controller.Demo || !uri.IsLoopback)) throw new ArgumentException("验证连接仅允许演示模式访问本机。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(Pref.Room.Trim().ToLowerInvariant(), "^[a-z0-9_-]{4,32}$")) throw new ArgumentException("房间码为 4–32 位字母、数字、_ 或 -。");
            if (string.IsNullOrWhiteSpace(Pref.Callsign) || Pref.Callsign.Trim().EnumerateRunes().Count() > 32) throw new ArgumentException("Callsign 需为 1–32 个字符。");
            uid ??= controller.Demo ? Guid.NewGuid().ToString("D") : Identity.LoadOrCreate(Controller.UserDir);
            await LeaveAsync();
            joinedRoom = Pref.Room.Trim().ToLowerInvariant();
            var currentGeneration = ++generation;
            showOnJoin = true;
            var next = new RoomClient(); client = next;
            next.StatusChanged += text => dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != currentGeneration || controller.IsClosing) return;
                Status = text;
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
                        if(showOnJoin){showOnJoin=false;controller.ShowRoomTasks();}
                        if (State.Map != controller.State.Map) controller.Notify("房间地图与本地不同 · 选择任务时请确认地图");
                    }
                    controller.UpdateRoomHistory(); Changed?.Invoke();
                }
            }));
            await next.StartAsync(uri, Profile("join"));
            controller.Save();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { Status = ex.Message; controller.Notify(Status); Changed?.Invoke(); }
    }
    public async Task LeaveAsync()
    {
        generation++;
        var old = client; client = null;
        Capture.Cancel(); State.Clear(); Status = "未加入房间";
        joinedRoom = null;
        lastProfile = lastOrigin = lastTarget = null;
        Changed?.Invoke();
        if (old != null) await old.DisposeAsync();
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
    async Task SynchronizeAsync()
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
