using System.IO;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WarDogs.Multiplayer;

namespace WarDogs;
public record BindingSetting(string Action,string Keys);
public record HistoryEntry(DateTime Time,string Message,PositionUpdate? Position=null,string Proximity="",SharedTaskHistory? Shared=null)
{
    public string Color=>Position==null?"#CAD3DF":Position.Role==Awaiting.Origin?"#91C5FF":"#FFBD87";
    public string Title=>Shared is {} shared?$"{Time:HH:mm:ss}  {shared.PublisherName} · 任务 {shared.Sequence} · {shared.Point.Coordinate}":Position==null?$"{Time:HH:mm:ss}  {Message}":$"{Time:HH:mm:ss}  {(Position.Role==Awaiting.Origin?"炮位":"目标")} · {Position.Coordinate}";
    public string Detail=>Position==null?"":$"{(Position.Map=="bakurani"?"Bakurani":"Ozeti")} · {(Shared!=null?Shared.Status:(Position.Weapon=="mortar"?"迫击炮":"SPH-2"))} · {Position.Source}\n{Proximity}";
    public string FullText=>Title+(Detail.Length>0?"\n"+Detail:"");
    public string Hint=>Position==null?Message:$"点击恢复{(Position.Role==Awaiting.Origin?"炮位（位置改变会清除当前目标）":"目标")}；不同地图将自动切换。\n{FullText}";
}
public class Preferences
{
    public MultiplayerPreferences Multiplayer{get;set;}=new();
    public bool AutoCheckUpdates{get;set;}=true;
    public Session Session{get;set;}=new();
    public Dictionary<string,string> Keys{get;set;}=new(){{"origin","Ctrl+Alt+1"},{"target","Ctrl+Alt+2"},{"pause","Ctrl+Alt+P"},{"hud","Ctrl+Alt+H"},{"mode","Ctrl+Alt+M"},{"map","Ctrl+Alt+G"}};
    public double HudLeft{get;set;}=double.NaN;public double HudTop{get;set;}=80;
    public double HudOpacity{get;set;}=0.5683815809559255;
    public double HudButtonOpacity{get;set;}=.75;
    public double HudTileOpacity{get;set;}=.75;
    public bool BubbleReadout{get;set;}=true;
    public string BubbleTextColor{get;set;}="#91C5FF";
    public string HudForm{get;set;}="panel";
    public double HudScale{get;set;}=1;
}
public class Controller
{
    public static readonly string Root=AppContext.BaseDirectory;
    public static readonly string UserDir=Path.Combine(Root,"UserData");
    public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public static readonly Dictionary<string,string> Actions=new(){{"origin","炮位设置"},{"target","目标选定"},{"pause","停止 / 恢复"},{"hud","隐藏 / 显示 HUD"},{"mode","模式切换"},{"map","地图切换"},{"publishTask","发布任务 / 取消等待"},{"roomTasks","任务菜单 / 关闭"},{"roomPrevious","任务菜单 · 上一条"},{"roomNext","任务菜单 · 下一条"},{"roomConfirm","任务菜单 · 确认"},{"roomCancel","任务菜单 · 取消"}};
    public Session State{get;private set;}
    public Ballistics Calculator{get;}
    public MainWindow Main=null!;public HudWindow Hud=null!;
    public Preferences Pref{get;}
    public bool Demo{get;}
    public UpdateService Updates{get;}
    public RoomCoordinator Rooms{get;}
    public TaskPickerWindow? TaskPicker{get;private set;}
    public void ShowRoomTasks()
    {
        if(TaskPicker!=null){TaskPicker.Show();return;}
        var picker=new TaskPickerWindow(this);TaskPicker=picker;picker.Closed+=(s,e)=>{if(ReferenceEquals(TaskPicker,picker))TaskPicker=null;};picker.Show();
    }
    bool dragging;PositionUpdate? dragUpdate;
    public bool Dragging{get=>dragging;set{dragging=value;if(!value&&dragUpdate is {} p){dragUpdate=null;RecordPosition(p);}}}
    public Coord? Pending{get;private set;}
    Awaiting pendingRole;
    public List<string> Notices{get;}=new();
    public ObservableCollection<HistoryEntry> History{get;}=new();
    public Dictionary<string,TowerInfo[]> Towers{get;}=new();
    public event Action? Updated;
    IntPtr handle;HwndSource? source;Dictionary<int,string> hotkeys=new();int nextId=100;
    readonly Dictionary<string,(uint Mod,uint Key)> registered=new();
    bool taskMenuHotkeys;
    static bool IsTaskMenuAction(string action)=>action is "roomPrevious" or "roomNext" or "roomConfirm" or "roomCancel";
    public void SetTaskMenuHotkeys(bool enabled)
    {
        taskMenuHotkeys=enabled;
        foreach(var action in Actions.Keys.Where(IsTaskMenuAction))
        {
            if(enabled)Bind(action,Pref.Keys.GetValueOrDefault(action,""),false);
            else{foreach(var old in hotkeys.Where(x=>x.Value==action).ToArray()){UnregisterHotKey(handle,old.Key);hotkeys.Remove(old.Key);}registered.Remove(action);}
        }
    }
    int requestRevision=0;uint lastSequence;bool closing;
    public bool IsClosing=>closing;
    public bool IsRecordingHotkey{get;set;}
    public event Action<string>? HotkeyRecorded;
    DispatcherTimer saveTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    public Controller(bool demo)
    {
        Demo=demo;Pref=new();Updates=new UpdateService(this);Updates.Changed+=()=>Updated?.Invoke();
        if(!demo)try{var path=Path.Combine(UserDir,"settings.json");if(File.Exists(path))Pref=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path),Json)??new();}catch{Notices.Add("设置读取失败 · 使用默认值");}
        Pref.Multiplayer??=new();
        if(Pref.Multiplayer.Role is not ("gunner" or "scout"))Pref.Multiplayer.Role="gunner";
        foreach(var (action,key) in new[]{("publishTask","Ctrl+Alt+3"),("roomTasks","Ctrl+Alt+T"),("roomPrevious","Ctrl+Alt+Up"),("roomNext","Ctrl+Alt+Down"),("roomConfirm","Ctrl+Alt+Enter"),("roomCancel","Ctrl+Alt+Escape")})Pref.Keys.TryAdd(action,key);
        State=Pref.Session;
        if(!Enum.IsDefined(State.Mode))State.Mode=InputMode.Smart;
        if(State.Map!="bakurani"&&State.Map!="ozeti")State.Map="bakurani";
        if(State.Weapon!="mortar"&&State.Weapon!="spg")State.Weapon="mortar";
        foreach(var id in new[]{"bakurani","ozeti"})if(!State.Maps.ContainsKey(id))State.Maps[id]=new();
        Calculator=new(Path.Combine(Root,"Data","weapons.json"));
        foreach(var id in new[]{"bakurani","ozeti"})
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"Data",id+".json")));
            Towers[id]=doc.RootElement.GetProperty("markers").EnumerateArray().Where(x=>x.GetProperty("icon").GetString()=="tower")
                .Select(x=>new TowerInfo(x.GetProperty("label").GetString()!,new(x.GetProperty("x").GetDouble()/100,x.GetProperty("y").GetDouble()/100))).ToArray();
        }
        State.Notice+=Notify;State.Changed+=Refresh;State.PositionUpdated+=p=>{if(Dragging&&p.Source=="地图")dragUpdate=p;else RecordPosition(p);};
        saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};
        Rooms=new RoomCoordinator(this);
        Rooms.Changed+=()=>Updated?.Invoke();
        if(demo){State.SetOrigin(new(80.52,69.85),"演示");State.SetTarget(new(82.92,71.65),"演示");}
        Notify(demo?"演示数据 · 未监听剪贴板，正式启动即可使用":"已就绪 · 设置炮位开始使用");
    }
    public Solution? Result=>State.Current.Origin is {} o&&State.Current.Target is {} t?Calculator.Solve(o,t,State.Weapon):null;
    public void Notify(string message)
    {
        if(History.Count==0||History[0].Position!=null||History[0].Message!=message)AddHistory(new(DateTime.Now,message));
        Updated?.Invoke();
    }
    void AddHistory(HistoryEntry entry){History.Insert(0,entry);while(History.Count>100)History.RemoveAt(History.Count-1);Notices.Insert(0,entry.FullText.Replace('\n',' '));if(Notices.Count>20)Notices.RemoveAt(20);}
    public void RecordRoomTask(RoomMember member,RoomTask task)
    {
        var shared=new SharedTaskHistory{RoomId=Rooms.State.RoomId,TaskId=task.Id,PublisherUid=member.Uid,Sequence=task.Sequence,Point=task.Point};shared.Remember(task.SolvedBy,Rooms.State);
        AddHistory(new(task.CreatedAt.LocalDateTime,"",new(task.Point.Map,State.Weapon,Awaiting.Target,task.Point.Coordinate,"房间任务"),TowerProximity.Describe(task.Point.Coordinate,Towers[task.Point.Map]),shared));
    }
    public void UpdateRoomHistory()
    {
        for(var i=0;i<History.Count;i++)if(History[i].Shared is {} shared&&shared.RoomId==Rooms.State.RoomId)
        {
            var before=shared.Status;shared.UpdateNames(Rooms.State);
            foreach(var member in Rooms.State.Members.Values)
            {
                foreach(var task in member.Tasks.Where(t=>t.Id==shared.TaskId))shared.Remember(task.SolvedBy,Rooms.State);
                if(member.Role=="gunner"&&member.Solved&&member.Target?.Same(shared.Point)==true)shared.Remember(new[]{member.Uid},Rooms.State);
            }
            if(shared.Status!=before)History[i]=History[i] with {};
        }
    }
    public void CancelInputForRoomSelection(){requestRevision++;Pending=null;dragUpdate=null;Rooms.Capture.Cancel();}
    void RecordPosition(PositionUpdate p){AddHistory(new(DateTime.Now,"",p,TowerProximity.Describe(p.Coordinate,Towers[p.Map])));Updated?.Invoke();}
    public void RestoreHistory(HistoryEntry entry)
    {
        if(entry.Shared is {} shared){Rooms.Select(shared.Point,"房间历史恢复");return;}
        if(entry.Position is not {} p)return;
        requestRevision++;Pending=null;dragUpdate=null;
        if(State.Map!=p.Map)State.ChangeMap();
        if(p.Role==Awaiting.Origin)State.SetOrigin(p.Coordinate,"历史恢复");else State.SetTarget(p.Coordinate,"历史恢复");
    }
    public void Refresh(){Updated?.Invoke();saveTimer.Stop();saveTimer.Start();}
    public void Save()
    {
        if(Demo)return;
        try{Directory.CreateDirectory(UserDir);if(Hud!=null){Pref.HudLeft=Hud.Left;Pref.HudTop=Hud.Top;}
            var options=new JsonSerializerOptions(Json){NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
            var path=Path.Combine(UserDir,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(Pref,options));File.Move(path+".tmp",path,true);
        }catch{Notify("设置暂未保存 · 请检查目录是否可写");}
    }
    public void InitializeNative(Window window,bool registerHotkeys=true)
    {
        handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(WndProc);
        lastSequence=GetClipboardSequenceNumber();
        if(!Demo&&!AddClipboardFormatListener(handle))Notify("剪贴板监听未启动 · 可使用手动输入");
        if(registerHotkeys)foreach(var action in Actions.Keys)Bind(action,Pref.Keys.GetValueOrDefault(action,""),false);
    }
    public async void Act(string action)
    {
        requestRevision++;
        if(action is "origin" or "target" or "pause" or "map" or "mode")Rooms.Capture.Cancel();
        switch(action)
        {
            case "publishTask":Rooms.TogglePublish(GetClipboardSequenceNumber());break;
            case "roomTasks":if(TaskPicker?.IsVisible==true)TaskPicker.Close();else ShowRoomTasks();break;
            case "roomPrevious":TaskPicker?.MoveSelection(-1);break;
            case "roomNext":TaskPicker?.MoveSelection(1);break;
            case "roomConfirm":TaskPicker?.Confirm();break;
            case "roomCancel":TaskPicker?.Close();break;
            case "origin":
                if(State.Waiting==Awaiting.Origin){State.OriginAction(null);return;}
                var o=await ReadClipboard(requestRevision);if(o.Current)State.OriginAction(o.Coord);break;
            case "target":var t=await ReadClipboard(requestRevision);if(t.Current)State.TargetAction(t.Coord);break;
            case "pause":State.TogglePause();break;
            case "hud":if(Hud.IsVisible)Hud.Hide();else Hud.Show();Notify(Hud.IsVisible?"HUD 已显示":"HUD 已隐藏 · 可从主窗口恢复");break;
            case "mode":State.ChangeMode();Pending=null;break;
            case "map":State.ChangeMap();Pending=null;break;
            case "weapon":State.ChangeWeapon();break;
            case "bubble":Hud.SetForm("bubble");break;
            case "compact":Hud.SetForm("compact");break;
            case "pending":if(Pending is {} p){Pending=null;if(pendingRole==Awaiting.Origin)State.SetOrigin(p);else State.SetTarget(p);Refresh();}break;
            case "discard":Pending=null;Refresh();break;
        }
    }
    async Task<(bool Current,Coord? Coord)> ReadClipboard(int revision)
    {
        for(int i=0;i<4;i++)
        {
            if(closing||revision!=requestRevision)return(false,null);
            try{return(true,Clipboard.ContainsText()?Coordinates.Parse(Clipboard.GetText()):null);}
            catch(COMException){await Task.Delay(40*(i+1));}
        }
        Notify("剪贴板正忙 · 本次未读取");return(revision==requestRevision,null);
    }
    async void ClipboardChanged()
    {
        var seq=GetClipboardSequenceNumber();if(seq==lastSequence)return;lastSequence=seq;
        if(Rooms.Capture.Waiting)
        {
            int publishRevision=++requestRevision;var copied=await ReadClipboard(publishRevision);
            if(!copied.Current)return;
            var coordinate=copied.Coord;
            if(coordinate!=null&&!new NetworkPoint(State.Map,coordinate.X,coordinate.Y).Valid)coordinate=null;
            if(Rooms.Capture.Consume(seq,coordinate) is {} published)await Rooms.PublishAsync(published);
            return;
        }
        if(State.Paused||State.Mode==InputMode.Manual||(State.Mode==InputMode.Smart&&State.Waiting==Awaiting.None))return;
        int revision=++requestRevision;var read=await ReadClipboard(revision);if(!read.Current)return;
        if(Dragging){if(read.Coord!=null){Pending=read.Coord;pendingRole=State.Waiting;Notify("地图拖动中收到坐标 · 可选择采用或忽略");}return;}
        State.OnClipboard(read.Coord);
    }
    IntPtr WndProc(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)
    {
        if(msg==0x031D&&!Demo)ClipboardChanged();
        if(msg==0x0312&&hotkeys.TryGetValue(w.ToInt32(),out var action)){if(IsRecordingHotkey)HotkeyRecorded?.Invoke(Pref.Keys.GetValueOrDefault(action,""));else Act(action);handled=true;}
        return IntPtr.Zero;
    }
    public bool Bind(string action,string input,bool feedback=true)
    {
        input=input.Trim();uint mod=0,key=0;
        if(input.Length>0)
        {
            try{var gesture=(KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(input)!;
                mod=(uint)gesture.Modifiers;key=(uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
                if(key==0||gesture.Key==Key.None)throw new FormatException();
            }catch{Notify("快捷键格式无效 · 使用 Ctrl+Alt+1 等格式");return false;}
            // WPF ModifierKeys and Win32 MOD_* use the same bit assignments.
            if(registered.Any(x=>x.Key!=action&&x.Value==(mod,key))){Notify("快捷键重复 · 原绑定保留");return false;}
            if(IsTaskMenuAction(action)&&!taskMenuHotkeys){Pref.Keys[action]=input;if(feedback){Notify("任务菜单快捷键已保存 · 菜单打开时启用");Save();}return true;}
            if(registered.TryGetValue(action,out var existing)&&existing==(mod,key)){if(feedback)Notify("快捷键未改变");return true;}
            int id=nextId++;
            if(!RegisterHotKey(handle,id,mod|0x4000,key)){Notify($"{Actions[action]}：快捷键被占用 · 原绑定保留");return false;}
            foreach(var old in hotkeys.Where(x=>x.Value==action).ToArray()){UnregisterHotKey(handle,old.Key);hotkeys.Remove(old.Key);}
            hotkeys[id]=action;registered[action]=(mod,key);
        }
        else{foreach(var old in hotkeys.Where(x=>x.Value==action).ToArray()){UnregisterHotKey(handle,old.Key);hotkeys.Remove(old.Key);}registered.Remove(action);}
        Pref.Keys[action]=input;if(feedback){Notify(input.Length==0?"快捷键已清除":$"{Actions[action]}：{input}");Save();}return true;
    }
    public void Manual(string text,bool origin)
    {
        Rooms.Capture.Cancel();
        requestRevision++;var c=Coordinates.Parse(text,true);if(c==null){Notify("未识别到唯一有效坐标 · 支持 x12.11 y11.11");return;}
        if(origin)State.SetOrigin(c,"手动");else State.SetTarget(c,"手动");
    }
    public void MapPoint(Coord c,bool origin)
    {
        if(!double.IsFinite(c.X)||!double.IsFinite(c.Y)||c.X<-.03||c.X>163.81||c.Y<-.01||c.Y>163.83)return;
        if(origin)State.SetOrigin(c,"地图");else State.SetTarget(c,"地图",true);
    }
    public void ShowMap(){Main.ShowMapPage(this,new RoutedEventArgs());Main.Show();Main.WindowState=WindowState.Normal;Main.Activate();}
    public async void Quit(){if(closing)return;closing=true;Save();TaskPicker?.Close();await Rooms.ShutdownAsync();RemoveClipboardFormatListener(handle);foreach(var id in hotkeys.Keys)UnregisterHotKey(handle,id);source?.RemoveHook(WndProc);Application.Current.Shutdown();}
    [DllImport("user32.dll")]static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")]static extern bool RemoveClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
}
