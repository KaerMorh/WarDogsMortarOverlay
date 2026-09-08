using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WarDogs;
public partial class MainWindow:Window
{
    readonly Controller c;bool ready;bool initializing;string mapMode="target";
    public MainWindow(Controller control)
    {
        c=control;InitializeComponent();c.Updated+=Render;Loaded+=async(s,e)=>await InitMap();
        Closing+=(s,e)=>{if(!c.IsClosing){e.Cancel=true;Hide();}};if(c.Demo)BuildLabel.Text="演示会话 · 未监听剪贴板";
        SettingsHost.Children.Add(new SettingsPanel(c));
        Render();
    }
    async Task InitMap()
    {
        if(initializing)return;initializing=true;
        try
        {

            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Controller.UserDir,c.Demo?"WebViewDemo":"WebView"));
            await MapView.EnsureCoreWebView2Async(env);
            MapView.CoreWebView2.SetVirtualHostNameToFolderMapping("wardogs.local",Path.Combine(Controller.Root,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
            MapView.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;MapView.CoreWebView2.Settings.AreDevToolsEnabled=false;
            MapView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled=false;
            MapView.CoreWebView2.NavigationStarting+=(s,e)=>{if(!e.Uri.StartsWith("https://wardogs.local/",StringComparison.Ordinal))e.Cancel=true;};
            MapView.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
            MapView.CoreWebView2.WebMessageReceived+=(s,e)=>
            {
                if(!e.Source.StartsWith("https://wardogs.local/",StringComparison.Ordinal))return;
                try{using var d=JsonDocument.Parse(e.WebMessageAsJson);var r=d.RootElement;var type=r.GetProperty("type").GetString();
                    if(type=="ready"){ready=true;SendMap();}
                    if(type=="drag"){c.Dragging=r.GetProperty("active").GetBoolean();}
                    if(type=="point")c.MapPoint(new(r.GetProperty("x").GetDouble(),r.GetProperty("y").GetDouble()),r.GetProperty("role").GetString()=="origin");
                    if(type=="error")c.Notify("部分地图瓦片未加载 · 坐标计算仍可用");
                }catch{c.Notify("地图消息未识别 · 状态未改变");}
            };
            MapView.CoreWebView2.Navigate("https://wardogs.local/index.html");
        }catch(Exception){MapHint.Text="地图未启动 · 请安装 Microsoft Edge WebView2 Runtime";c.Notify("地图组件未启动 · 剪贴板和 HUD 仍可使用");}
    }
    void SendMap()
    {
        if(!ready)return;
        var s=c.State;MapView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{type="state",map=s.Map,origin=s.Current.Origin,target=s.Current.Target,towers=TowerMarkers(),mode=mapMode,maxRange=c.Calculator.Weapons[s.Weapon].MaxRangeKm*10},Controller.Json));
    }
    void Render()
    {
        SettingsNav.Content=UpdatePanel.Badge("设置",c.Updates.HasUpdate);
        var s=c.State;var r=c.Result;MapTitle.Text=s.Map=="bakurani"?"BAKURANI":"OZETI";
        Distance.Text=r?.Distance.ToString("0")??"—";Azimuth.Text=r?.Azimuth?.ToString("0.0")??"—";
        Elevation.Text=r==null?"—":s.Weapon=="mortar"?r.Single?.ToString()??"—":$"{r.Low?.ToString()??"—"} / {r.High?.ToString()??"—"}";
        ArcLabel.Text=s.Weapon=="mortar"?"仰角 / MIL":"低抛 / 高抛 · MIL";
        RangeStatus.Text=r?.Status??"等待炮位与目标";
        ModeButton.Content=s.ModeText+" ↻";WeaponButton.Content=(s.Weapon=="mortar"?"迫击炮":"SPH-2")+" ↻";MapSwitchButton.Content=(s.Map=="bakurani"?"Bakurani":"Ozeti")+" ↻";
        StateLabel.Text="●  "+s.Status;OriginLabel.Text=s.Current.Origin?.ToString()??"尚未设置";TargetLabel.Text=s.Current.Target?.ToString()??"尚未设置";
        SourceLabel.Text=$"来源 {s.Current.Source}   ·   {s.Current.Updated?.ToString("HH:mm:ss")??"—"}";
        PendingPanel.Visibility=c.Pending!=null?Visibility.Visible:Visibility.Collapsed;
        NoticeList.Text=string.Join("\n",c.Notices.Take(2));SendMap();
    }
    void ActionClick(object sender,RoutedEventArgs e)=>c.Act((string)((Button)sender).Tag);
    void QuitClick(object s,RoutedEventArgs e)=>c.Quit();
    void BackToHud(object s,RoutedEventArgs e){if(!c.Hud.IsVisible)c.Hud.Show();c.Hud.Activate();Hide();}
    public void ShowMapPage(object s,RoutedEventArgs e){MapPage.Visibility=Visibility.Visible;SettingsPage.Visibility=Visibility.Collapsed;MapNav.Background=(Brush)new BrushConverter().ConvertFromString("#2D507C")!;SettingsNav.Background=(Brush)new BrushConverter().ConvertFromString("#252D3B")!;}
    void ShowSettingsPage(object s,RoutedEventArgs e){MapPage.Visibility=Visibility.Collapsed;SettingsPage.Visibility=Visibility.Visible;MapNav.Background=(Brush)new BrushConverter().ConvertFromString("#252D3B")!;SettingsNav.Background=(Brush)new BrushConverter().ConvertFromString("#2D507C")!;}
    object[] TowerMarkers()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Controller.Root,"Data",c.State.Map+".json")));
        return doc.RootElement.GetProperty("markers").EnumerateArray().Where(x=>x.GetProperty("icon").GetString()=="tower").Select(x=>(object)new {x=x.GetProperty("x").GetDouble()/100,y=x.GetProperty("y").GetDouble()/100,label=x.GetProperty("label").GetString()}).ToArray();
    }
    void ManualOrigin(object s,RoutedEventArgs e)=>c.Manual(ManualInput.Text,true);
    void ManualTarget(object s,RoutedEventArgs e)=>c.Manual(ManualInput.Text,false);
    void MapOriginClick(object s,RoutedEventArgs e){mapMode="origin";MapHint.Text="点击设置炮位 · 炮位改变后请重新选定目标";MapOriginBtn.Background=(Brush)new BrushConverter().ConvertFromString("#2D507C")!;MapTargetBtn.Background=(Brush)new BrushConverter().ConvertFromString("#252D3B")!;SendMap();}
    void MapTargetClick(object s,RoutedEventArgs e){mapMode="target";MapHint.Text="点击放置目标 · 拖动标记实时更新 · 滚轮缩放";MapTargetBtn.Background=(Brush)new BrushConverter().ConvertFromString("#2D507C")!;MapOriginBtn.Background=(Brush)new BrushConverter().ConvertFromString("#252D3B")!;SendMap();}
    void FitClick(object s,RoutedEventArgs e){if(ready)MapView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"fit\"}");}
    public async Task VerifyAndCapture()
    {
        var dir=Path.Combine(Controller.Root,"Verification");Directory.CreateDirectory(dir);
        var log=new List<string>();
        void Check(bool ok,string text){if(!ok)throw new Exception(text);log.Add("PASS "+text);}
        try
        {
            var defaults=new Preferences();Check(defaults.BubbleReadout&&defaults.HudOpacity==0.5683815809559255&&defaults.HudButtonOpacity==.75&&defaults.HudTileOpacity==.75,"recorded opacity defaults and enabled bubble readout");
            for(int n=0;n<100&&!ready;n++)await Task.Delay(100);
            Check(ready,"WebView2 local map ready");
            Check(c.Result?.Single?.ToString()=="690","HUD initial demo 300m -> 690 MIL");
            await MapView.CoreWebView2.ExecuteScriptAsync("paint()");
            var loaded="0";for(int n=0;n<100&&loaded=="0";n++){await Task.Delay(100);loaded=await MapView.CoreWebView2.ExecuteScriptAsync("[...cache.values()].filter(i=>i.complete&&i.naturalWidth>0).length");}
            Check(int.Parse(loaded)>0,"Bakurani offline tiles loaded");
            Check(await MapView.CoreWebView2.ExecuteScriptAsync("state.towers.length===5 && state.towers.some(t=>t.label==='Tower 1'&&t.x===80.52)")=="true","Bakurani tower markers and coordinate conversion");
            await MapView.CoreWebView2.ExecuteScriptAsync("(()=>{const p=screen({x:83.57,y:69.85});setPoint('target',p.x,p.y);return true})()");
            await Task.Delay(200);
            Check(c.State.Current.Target==new Coord(83.57,69.85)&&c.Result?.Single?.ToString()=="685","WebView point -> C# -> HUD 305m / 685 MIL");
            c.Manual("x83.52 y69.85大大的asadasd",false);await Task.Delay(100);
            Check(c.State.Current.Target==new Coord(83.52,69.85),"manual suffix parser through UI controller");
            var target=await MapView.CoreWebView2.ExecuteScriptAsync("state.target.x");Check(Math.Abs(double.Parse(target,System.Globalization.CultureInfo.InvariantCulture)-83.52)<1e-8,"C# target -> WebView marker");
            c.State.ChangeMap();await Task.Delay(900);
            var oz=await MapView.CoreWebView2.ExecuteScriptAsync("state.map==='ozeti' && [...cache.values()].some(i=>i.complete&&i.naturalWidth>0)");Check(oz=="true","Ozeti offline tiles and map switch");
            Check(await MapView.CoreWebView2.ExecuteScriptAsync("state.towers.length===4")=="true","Ozeti tower markers");
            c.State.ChangeMap();Check(c.Result!=null,"map positions restored directly without confirmation");
            c.State.SetTarget(new(90,70),"验证");Check(c.Result is {InRange:false,Single:null},"out of range clears MIL");
            Check(!c.Bind("target",c.Pref.Keys["origin"]),"duplicate hotkey rejected without popup");
            c.Act("hud");Check(!c.Hud.IsVisible,"HUD hides");c.Act("hud");Check(c.Hud.IsVisible,"HUD restores");
            c.State.SetOrigin(new(80.52,69.85),"演示");c.State.SetTarget(new(82.92,71.65),"演示");
            FitClick(this,new RoutedEventArgs());await Task.Delay(900);
            c.Hud.Left=SystemParameters.WorkArea.Right-c.Hud.ActualWidth-35;c.Hud.Top=100;
            c.Hud.SetForm("compact");await Task.Delay(100);Check(c.Hud.ActualWidth<=350&&c.Hud.ActualHeight<240,"simplified HUD dimensions");
            c.Pref.BubbleReadout=false;c.Hud.SetForm("bubble");await Task.Delay(100);Check(c.Hud.ActualWidth==20&&c.Hud.ActualHeight==20,$"20px collapsed bubble (actual {c.Hud.ActualWidth}x{c.Hud.ActualHeight})");
            c.Hud.SetForm("panel");await Task.Delay(100);Check(c.Hud.ActualWidth<=360&&c.Hud.ActualHeight<390,$"compact panel dimensions after separate origin line ({c.Hud.ActualWidth:0}x{c.Hud.ActualHeight:0})");
            var before=c.History.Count;c.Dragging=true;c.MapPoint(new(81.1,70.1),false);c.MapPoint(new(81.2,70.2),false);Check(c.History.Count==before,"map drag does not flood history");c.Dragging=false;
            Check(c.History.Count==before+1&&c.History[0].Position?.Coordinate==new Coord(81.2,70.2),"map drag records final point");
            var saved=c.History[0];Check(saved.Proximity.Contains("T1"),"coordinate record includes nearby Tower");
            c.State.ChangeMap();c.RestoreHistory(saved);Check(c.State.Map=="bakurani"&&c.State.Current.Target==saved.Position!.Coordinate,"history restores target and its map");
            var oldOrigin=c.History.First(h=>h.Position?.Role==Awaiting.Origin);c.State.SetOrigin(new(78,78));c.RestoreHistory(oldOrigin);Check(c.State.Current.Origin==oldOrigin.Position!.Coordinate&&c.State.Current.Target==null,"history restores origin and clears stale target");
            c.State.SetTarget(new(81.52,70.85),"演示");
            IEnumerable<Button> Buttons(DependencyObject root)
            {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is Button button)yield return button;foreach(var nested in Buttons(child))yield return nested;}
            }
            c.Hud.ExpandHistory();await Task.Delay(100);
            var historyButton=Buttons(c.Hud).First(b=>b.DataContext is HistoryEntry {Position.Role:Awaiting.Target});var chosen=(HistoryEntry)historyButton.DataContext;
            historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.State.Current.Target==chosen.Position!.Coordinate&&c.State.Current.Source=="历史恢复","actual history button restores coordinate");
            c.Hud.SetForm("compact");Buttons(c.Hud).First(b=>b.Content as string=="还原界面").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.Hud.Form=="panel","dedicated restore button works");
            Buttons(c.Hud).First(b=>b.Content is StackPanel badge&&badge.Children.OfType<TextBlock>().Any(t=>t.Text=="设置")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Buttons(c.Hud).First(b=>b.Content as string=="本次记录").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            c.Hud.UpdateLayout();Check(Buttons(c.Hud).Any(b=>b.DataContext is HistoryEntry),"settings history uses the same clickable records");
            Buttons(c.Hud).First(b=>b.Content as string=="快捷键 / 外观").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            IEnumerable<TextBlock> Texts(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is TextBlock text)yield return text;foreach(var nested in Texts(child))yield return nested;}}
            IEnumerable<TextBox> EnumerableTextBoxes(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is TextBox text)yield return text;foreach(var nested in EnumerableTextBoxes(child))yield return nested;}}
            c.Hud.SetForm("compact");c.Hud.UpdateLayout();Check(!Texts(c.Hud).Any(t=>t.Text.Contains("Ctrl+Alt")),"simplified buttons hide shortcut labels");
            var actionButtons=Buttons(c.Hud).Where(b=>b.Tag is string).ToArray();Check(actionButtons.Length==5&&actionButtons.Select(b=>Math.Round(b.TranslatePoint(new Point(),c.Hud).Y)).Distinct().Count()==1,"simplified actions share one row");
            c.State.SetTarget(new(90,90));foreach(var form in new[]{"panel","compact"}){c.Hud.SetForm(form);Check(Texts(c.Hud).Any(t=>t.Text.StartsWith("目标  x90.00")),"target remains visible out of range in "+form);}
            c.State.SetOrigin(new(80.52,69.85),"验证");c.State.SetTarget(new(81.52,70.85),"验证");c.Hud.SetForm("compact");c.Hud.UpdateLayout();
            string? ActionText(Button b)=>(b.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            var originAction=Buttons(c.Hud).First(b=>ActionText(b)=="设炮位");
            Check(originAction.Background is SolidColorBrush {Color.R:35,Color.G:46,Color.B:64}&&originAction.BorderBrush==Brushes.Transparent,"origin button keeps its normal background after origin is set");
            c.State.Current.Origin=null;c.Refresh();Check(originAction.Background is SolidColorBrush {Color.R:35,Color.G:46,Color.B:64}&&originAction.BorderBrush==Brushes.Transparent,"origin button keeps its original background when no origin is set");c.State.SetOrigin(new(80.52,69.85),"验证");c.State.SetTarget(new(81.52,70.85),"验证");
            c.State.OriginAction(null);Check(originAction.Background is SolidColorBrush {Color.R:169,Color.G:120,Color.B:34}&&originAction.BorderBrush==Brushes.Transparent,"origin button has yellow background while waiting for copied coordinate");c.State.OriginAction(null);
            var originLine=Texts(c.Hud).First(t=>t.Text.StartsWith("炮位  x80.52"));var targetLine=Texts(c.Hud).First(t=>t.Text.StartsWith("目标  x81.52"));
            Check(targetLine.TranslatePoint(new Point(),c.Hud).Y<originLine.TranslatePoint(new Point(),c.Hud).Y,"persistent target line is restored with a separate origin line below");
            typeof(HudWindow).GetMethod("OpenCoordinateInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(c.Hud,new object[]{false});c.Hud.UpdateLayout();
            var quickInput=EnumerableTextBoxes(c.Hud).Single(t=>t.IsVisible);quickInput.Text="x82.52 y71.85";Buttons(c.Hud).First(b=>b.IsVisible&&b.Content as string=="设目标").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(c.State.Current.Target==new Coord(82.52,71.85)&&!quickInput.IsVisible,"double-click coordinate editor accepts pasted target through confirm button");
            c.Pref.HudButtonOpacity=0;c.Pref.HudTileOpacity=0;c.Refresh();Check(Buttons(c.Hud).All(b=>b.Background is SolidColorBrush {Color.A:0}),"button backgrounds independently reach zero opacity");
            var metricLabel=Texts(c.Hud).First(t=>t.Text=="距离 m");var tile=(Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(metricLabel));Check(tile.Background is SolidColorBrush {Color.A:0}&&metricLabel.Opacity==1,"tile background opacity does not fade text");
            c.Pref.HudButtonOpacity=1;c.Pref.HudTileOpacity=.94;c.State.SetTarget(new(81.52,70.85),"演示");
            c.Hud.SetForm("bubble");var menu=c.Hud.BubbleMenu();menu.Items.OfType<MenuItem>().First(m=>m.Header.ToString()!.StartsWith("小球极简化")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(c.Pref.BubbleReadout&&Texts(c.Hud).Any(t=>t.Text=="141(m)  839 MIL  45.0 度"),"bubble menu enables units-only readout");
            var inputMenu=c.Hud.BubbleMenu().Items.OfType<MenuItem>().First(m=>m.Header.ToString()=="输入模式");inputMenu.Items.OfType<MenuItem>().First(m=>m.Header.ToString()=="精确手动").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(c.State.Mode==InputMode.Manual,"bubble menu selects explicit input mode");
            c.Hud.BubbleMenu().Items.OfType<MenuItem>().First(m=>m.Header is StackPanel badge&&badge.Children.OfType<TextBlock>().Any(t=>t.Text=="设置")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(c.Hud.Form=="settings","bubble menu opens settings");
            Buttons(c.Hud).First(b=>b.Content as string=="返回界面").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(c.Hud.Form=="bubble","settings returns to bubble");c.Pref.BubbleReadout=false;c.State.Mode=InputMode.Smart;
            c.Pref.BubbleReadout=true;c.Hud.OpenSettings();Buttons(c.Hud).First(b=>b.Content as string=="橙色").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));c.Hud.SetForm("bubble");
            Check(Texts(c.Hud).Any(t=>t.Text.Contains(" MIL ")&&t.Foreground is SolidColorBrush {Color.R:255,Color.G:189,Color.B:135}),"bubble text color updates from settings");c.Pref.BubbleTextColor="#91C5FF";c.Pref.BubbleReadout=false;
            c.Hud.SetForm("panel");await Task.Delay(100);
            var preview=new UpdateManifest("0.7.0","示例更新：改进地图交互，优化使用体验。",UpdateManifest.Repository+"/releases/download/v0.7.0/WarDogsOverlay-0.7.0-win-x64-Setup.exe",100,new string('0',64));
            typeof(UpdateService).GetProperty(nameof(UpdateService.Available))!.SetValue(c.Updates,preview);
            typeof(UpdateService).GetProperty(nameof(UpdateService.Status))!.SetValue(c.Updates,"发现新版本 0.7.0");
            c.Refresh();
            bool HasBadge(object content)=>content is StackPanel badge&&badge.Children.OfType<Border>().Any(b=>b.Visibility==Visibility.Visible&&b.Background==Brushes.OrangeRed);
            Check(HasBadge(SettingsNav.Content),"main settings red dot for available update");
            foreach(var form in new[]{"panel","compact"}){c.Hud.SetForm(form);Check(Buttons(c.Hud).Any(b=>HasBadge(b.Content)),"settings red dot in "+form);}
            Check(c.Hud.BubbleMenu().Items.OfType<MenuItem>().Any(m=>HasBadge(m.Header)),"bubble menu settings red dot");
            c.Hud.OpenSettings();c.Hud.UpdateLayout();Check(Texts(c.Hud).Any(t=>t.Text.StartsWith("软件更新"))&&Buttons(c.Hud).Any(b=>b.Content as string=="下载更新"&&b.IsVisible),"settings exposes available update and download action");
            c.Hud.SetForm("panel");
            await ExportVisuals(dir);log.Add("PASS native WPF and WebView captures exported");
            typeof(UpdateService).GetProperty(nameof(UpdateService.Available))!.SetValue(c.Updates,null);c.Refresh();
            Check(!HasBadge(SettingsNav.Content)&&!Buttons(c.Hud).Any(b=>HasBadge(b.Content)),"settings red dot clears when update is no longer available");
            File.WriteAllLines(Path.Combine(dir,"integration.txt"),log);
            c.Notify("集成验证完成 · 演示数据，可直接拖动地图标记");
        }
        catch(Exception ex){log.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(dir,"integration.txt"),log);c.Notify("集成验证未通过 · 详见 Verification/integration.txt");}
    }
    async Task ExportVisuals(string dir)
    {
        using(var f=File.Create(Path.Combine(dir,"map.png")))await MapView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);
        UpdateLayout();c.Hud.UpdateLayout();
        static RenderTargetBitmap Render(FrameworkElement el){var v=new DrawingVisual();using(var dc=v.RenderOpen())dc.DrawRectangle(new VisualBrush(el),null,new Rect(0,0,el.ActualWidth,el.ActualHeight));var r=new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth),(int)Math.Ceiling(el.ActualHeight),96,96,PixelFormats.Pbgra32);r.Render(v);return r;}
        static void Save(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var f=File.Create(path);encoder.Save(f);}
        var root=(FrameworkElement)Content;var wpf=Render(root);var map=new BitmapImage();map.BeginInit();map.CacheOption=BitmapCacheOption.OnLoad;map.UriSource=new Uri(Path.Combine(dir,"map.png"));map.EndInit();
        var dv=new DrawingVisual();using(var dc=dv.RenderOpen()){dc.DrawImage(wpf,new Rect(0,0,root.ActualWidth,root.ActualHeight));var pt=MapView.TranslatePoint(new Point(0,0),root);dc.DrawImage(map,new Rect(pt.X,pt.Y,MapView.ActualWidth,MapView.ActualHeight));}
        var composite=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);composite.Render(dv);Save(composite,Path.Combine(dir,"workbench.png"));Save(Render((FrameworkElement)c.Hud.Content),Path.Combine(dir,"hud.png"));
        foreach(var form in new[]{"compact","bubble","settings"}){c.Hud.SetForm(form);await Task.Delay(100);Save(Render((FrameworkElement)c.Hud.Content),Path.Combine(dir,"hud-"+form+".png"));}
        c.Pref.BubbleReadout=true;c.Hud.SetForm("bubble");await Task.Delay(100);Save(Render((FrameworkElement)c.Hud.Content),Path.Combine(dir,"hud-bubble-readout.png"));
        var bubbleMenu=c.Hud.BubbleMenu();bubbleMenu.PlacementTarget=c.Hud;bubbleMenu.IsOpen=true;await Task.Delay(100);Save(Render(bubbleMenu),Path.Combine(dir,"bubble-menu.png"));bubbleMenu.IsOpen=false;c.Pref.BubbleReadout=false;
        c.Hud.SetForm("panel");c.Hud.ExpandHistory();await Task.Delay(100);Save(Render((FrameworkElement)c.Hud.Content),Path.Combine(dir,"hud-history.png"));
        foreach(var form in new[]{"panel","compact"})
        {
            c.Pref.HudOpacity=.25;c.Hud.SetForm(form);await Task.Delay(100);var hud=(FrameworkElement)c.Hud.Content;
            var white=new DrawingVisual();using(var dc=white.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,hud.ActualWidth,hud.ActualHeight));dc.DrawImage(Render(hud),new Rect(0,0,hud.ActualWidth,hud.ActualHeight));}
            var onWhite=new RenderTargetBitmap((int)Math.Ceiling(hud.ActualWidth),(int)Math.Ceiling(hud.ActualHeight),96,96,PixelFormats.Pbgra32);onWhite.Render(white);Save(onWhite,Path.Combine(dir,"hud-"+form+"-white.png"));
        }
        var tip=new ToolTip{Content="设置炮位 / 等待新坐标\n快捷键：Ctrl+Alt+1",PlacementTarget=c.Hud,IsOpen=true};await Task.Delay(200);tip.UpdateLayout();Save(Render(tip),Path.Combine(dir,"tooltip.png"));tip.IsOpen=false;c.Pref.HudOpacity=1;
        ShowSettingsPage(this,new RoutedEventArgs());UpdateLayout();await Task.Delay(100);Save(Render(root),Path.Combine(dir,"settings.png"));ShowMapPage(this,new RoutedEventArgs());c.Hud.SetForm("panel");
    }
}

