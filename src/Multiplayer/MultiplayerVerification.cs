using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WarDogs.Multiplayer;

internal static class MultiplayerVerification
{
    internal static async Task RunAsync(Controller c, string? serverUrl = null)
    {
        var directory = Path.Combine(Controller.Root, "Verification", "multiplayer"); Directory.CreateDirectory(directory);
        var log = new List<string>();
        void Check(bool pass, string text) { if (!pass) throw new Exception(text); log.Add("PASS " + text); }
        try
        {
            if (serverUrl != null) { await LiveAsync(c, new Uri(serverUrl), Check); c.History.Clear(); }
            var staleSettings=new RoomSettingsPanel(c);c.Pref.Multiplayer.ShareTarget=false;
            var staleName=staleSettings.Children.OfType<TextBox>().Last();staleName.Text="Updated Callsign";
            staleName.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,0,staleName,staleSettings){RoutedEvent=System.Windows.Input.Keyboard.LostKeyboardFocusEvent});
            Check(!c.Pref.Multiplayer.ShareTarget,"editing another setting cannot reenable sharing from stale UI");
            c.Pref.Multiplayer.ShareTarget=true;
            var members = new List<RoomMember>();
            for (var i = 0; i < 3; i++)
            {
                var m = new RoomMember { Uid = Guid.NewGuid().ToString("D"), SessionId = Guid.NewGuid().ToString("D"), Callsign = i < 2 ? "观察员" : "炮组 Alpha", DisplayName = i < 2 ? "观察员#" + (i + 1) : "炮组 Alpha", Role = i < 2 ? "scout" : "gunner", Map = "bakurani", Joined = i + 1, Online = i != 1, OfflineAt = i == 1 ? DateTimeOffset.UtcNow : null };
                for (var t = 0; t < 3; t++) m.Tasks.Add(new() { Id = Guid.NewGuid().ToString("D"), Sequence = 9 - i * 3 - t, CreatedAt = DateTimeOffset.Now.AddSeconds(-t * 20), Point = new("bakurani", 81.52 + i * .3 + t * .2, 70.85) });
                members.Add(m);
            }
            members[2].Origin = new("bakurani", 80.52, 69.85); members[2].Target = members[0].Tasks[0].Point; members[2].Solved = true;
            c.Rooms.State.Apply(new() { V = Protocol.Version, Type = "snapshot", RoomId = "preview", Room = "demo", Map = "bakurani", Revision = 1, SessionId = "preview", Members = members, Identities = members.Select(m => new RoomIdentity(m.Uid, m.DisplayName)).ToList() });
            c.UpdateRoomHistory(); c.Refresh();
            Check(c.History.Count(h => h.Shared != null) == 9, "all nine tasks recorded once");
            c.Rooms.State.Apply(new() { V = Protocol.Version, Type = "member", RoomId = "preview", Revision = 2, Member = members[0] }); c.UpdateRoomHistory();
            Check(c.History.Count(h => h.Shared != null) == 9, "repeated member does not duplicate history");
            var before = c.State.Current.Target;
            c.Rooms.Capture.Toggle(100); var published = c.Rooms.Capture.Consume(101, new(82, 71));
            Check(published != null && c.State.Current.Target == before, "publish capture does not change local target");
            c.Rooms.Select(members[0].Tasks[0].Point, "房间任务验证");
            Check(c.State.Current.Target == members[0].Tasks[0].Point.Coordinate, "room selection updates local solver");
            foreach (var form in new[] { "panel", "compact" })
            {
                c.Hud.SetForm(form); await Task.Delay(100); Save((FrameworkElement)c.Hud.Content, Path.Combine(directory, form + ".png"));
                Check(c.Hud.ActualWidth <= 360 && c.Hud.ActualHeight < 760, form + " bounded HUD dimensions");
            }
            c.ShowRoomTasks(); await Task.Delay(250);
            if (c.TaskPicker != null)
            {
                var picker=c.TaskPicker;
                Check(picker.WindowStyle==WindowStyle.ToolWindow&&!ReferenceEquals(picker,c.Hud),"task selection is an independent draggable tool window");
                var oldHudLeft=c.Hud.Left;picker.Left-=20;Check(c.Hud.Left==oldHudLeft,"moving task window does not move HUD");
                picker.MoveSelection(1);var highlighted=picker.HighlightedTaskId;
                var fresh=new RoomTask{Id=Guid.NewGuid().ToString("D"),Sequence=10,CreatedAt=DateTimeOffset.Now,Point=new("bakurani",82.2,71.1)};
                members[0].Tasks.Insert(0,fresh);members[0].Tasks.RemoveAt(3);
                c.Rooms.State.Apply(new(){V=Protocol.Version,Type="member",RoomId="preview",Revision=3,Member=members[0]});c.Refresh();await Task.Delay(250);
                Check(highlighted!=null&&picker.HighlightedTaskId==highlighted,"new task preserves selected task identity");
                Save((FrameworkElement)picker.Content, Path.Combine(directory, "task-picker.png"));
                members[0].Tasks.RemoveAll(t=>t.Id==highlighted);
                c.Rooms.State.Apply(new(){V=Protocol.Version,Type="member",RoomId="preview",Revision=4,Member=members[0]});c.Refresh();await Task.Delay(250);
                Check(picker.HighlightedTaskId==null,"removing selected task does not select a different target");
                picker.Close();
            }
            Check(c.Hud.ActualHeight < 240, "task module does not enlarge compact HUD");
            c.Pref.Multiplayer.Role = "scout"; c.Refresh(); await Task.Delay(100);
            Save((FrameworkElement)c.Hud.Content, Path.Combine(directory, "scout.png"));
            Check(Texts(c.Hud).Any(t => t.Text == "距离 m"), "role changes preserve original HUD layout");
            c.Hud.OpenSettings("room"); await Task.Delay(100); Save((FrameworkElement)c.Hud.Content, Path.Combine(directory, "settings.png"));
            for (var i = 0; i < 130; i++) c.Notify("历史限额验证 " + i);
            Check(c.History.Count == 100, "local and room history share strict 100 cap");
            Check(!File.Exists(Path.Combine(Controller.UserDir, "identity.json")), "verification does not persist identity");
            log.Add("PASS multiplayer UI verification complete");
        }
        catch (Exception ex) { log.Add("FAIL " + ex); }
        File.WriteAllLines(Path.Combine(directory, "verification.txt"), log);
    }
    static async Task LiveAsync(Controller c, Uri uri, Action<bool, string> check)
    {
        await using var observer = new RoomClient();
        var observed = new ConcurrentDictionary<string, RoomMember>();
        observer.Received += e => { if (e.Type == "snapshot") foreach (var member in e.Members) observed[member.Uid] = member; else if (e.Type == "member" && e.Member != null) observed[e.Member.Uid] = e.Member; };
        var room = "verify-" + Guid.NewGuid().ToString("N")[..8];
        var pref = c.Pref.Multiplayer; pref.Room = room; pref.ServerUrl = uri.ToString(); pref.Callsign = "UI Gun";
        async Task Until(Func<bool> predicate, string label)
        {
            var watch = Stopwatch.StartNew(); while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new Exception("Timeout: " + label); await Task.Delay(25); } check(true, label);
        }
        await observer.StartAsync(uri, new() { Type = "join", Uid = Guid.NewGuid().ToString("D"), Callsign = "Observer", Room = room, Role = "scout", Map = "bakurani", Weapon = "mortar" });
        var joinWatch=Stopwatch.StartNew();var firstJoin=c.Rooms.JoinForVerificationAsync();var duplicateJoin=c.Rooms.JoinForVerificationAsync();
        check(ReferenceEquals(firstJoin,duplicateJoin),"duplicate join clicks share one operation");
        await firstJoin;
        check(joinWatch.Elapsed>=TimeSpan.FromMilliseconds(900)&&c.Rooms.JoinButtonText=="已加入","joined display waits one second");
        try
        {
            await Until(() => observer.Connected && c.Rooms.Connected && c.Rooms.State.SessionId.Length > 0, "WPF adapter joins real Go server");
            var sessionId=c.Rooms.State.SessionId;await c.Rooms.RequestSyncAsync();
            check(c.Rooms.State.SessionId==sessionId,"manual sync preserves active session");
            var validRoom=pref.Room;pref.Room="bad!";var failureWatch=Stopwatch.StartNew();await c.Rooms.JoinForVerificationAsync();
            check(failureWatch.Elapsed>=TimeSpan.FromMilliseconds(2900)&&c.Rooms.Connected,"invalid switch holds failure for three seconds without dropping current room");
            pref.Room=validRoom;c.Rooms.PreferencesChanged();
            var uid = c.Rooms.Uid!;
            var initial = c.State.Current.Target!;
            await Until(() => observed.TryGetValue(uid, out var member) && member.Target?.Coordinate == initial && member.Solved, "automatic target and valid solution shared");
            var published = new Coord(81.8, 70.2);
            await c.Rooms.PublishAsync(published);
            await Until(() => observed.TryGetValue(uid, out var member) && member.Tasks.Any(t => t.Point.Coordinate == published), "independent publish reaches observer");
            check(c.State.Current.Target == initial, "live publish preserves local target");
            pref.ShareTarget = false; c.Rooms.PreferencesChanged(); c.State.SetTarget(new(82.1, 70.9), "隐私验证");
            await Task.Delay(1200);
            check(observed[uid].Target?.Coordinate == initial, "disabling sharing retains last public target without leaking changes");
            // State messages have no share flag, and the server cannot infer the local preference.
            pref.ShareTarget = true; c.Rooms.PreferencesChanged();
            await Until(() => observed[uid].Target?.Coordinate == c.State.Current.Target, "reenabling sharing publishes latest target");
            await c.Rooms.LeaveAsync();
            await Until(() => !observed[uid].Online, "explicit leave appears offline to observer");
            await c.Rooms.JoinForVerificationAsync();
            await Until(() => observed.TryGetValue(uid, out var member) && member.Online && member.Tasks.Count == 0, "WPF rejoin replaces same UID without restoring tasks");
            var leaving=c.Rooms.LeaveAsync();var rapidRejoin=c.Rooms.JoinForVerificationAsync();
            await Task.WhenAll(leaving,rapidRejoin);
            await Until(() => c.Rooms.Connected&&observed.TryGetValue(uid,out var member)&&member.Online,"rapid leave and rejoin keeps newest intent");
        }
        finally { await c.Rooms.LeaveAsync(); }
    }
    static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is TextBlock text) yield return text; foreach (var nested in Texts(child)) yield return nested; }
    }
    static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
