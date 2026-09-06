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
    void ShowMapPage(object s,RoutedEventArgs e){MapPage.Visibility=Visibility.Visible;SettingsPage.Visibility=Visibility.Collapsed;MapNav.Background=(Brush)new BrushConverter().ConvertFromString("#2D507C")!;SettingsNav.Background=(Brush)new BrushConverter().ConvertFromString("#252D3B")!;}
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
            for(int n=0;n<100&&!ready;n++)await Task.Delay(100);
            Check(ready,"WebView2 local map ready");
            Check(c.Result?.Single?.ToString()=="690","HUD initial demo 300m -> 690 MIL");
            await Task.Delay(900);
            var loaded=await MapView.CoreWebView2.ExecuteScriptAsync("[...cache.values()].filter(i=>i.complete&&i.naturalWidth>0).length");
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
            c.Hud.SetForm("compact");await Task.Delay(100);Check(c.Hud.ActualWidth<=320&&c.Hud.ActualHeight<85,"transparent HUD dimensions");
            c.Hud.SetForm("bubble");await Task.Delay(100);Check(c.Hud.ActualWidth==40&&c.Hud.ActualHeight==40,"40px collapsed bubble");
            c.Hud.SetForm("panel");await Task.Delay(100);Check(c.Hud.ActualWidth<=360&&c.Hud.ActualHeight<270,"compact panel dimensions");
            await ExportVisuals(dir);log.Add("PASS native WPF and WebView captures exported");
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
        ShowSettingsPage(this,new RoutedEventArgs());UpdateLayout();await Task.Delay(100);Save(Render(root),Path.Combine(dir,"settings.png"));ShowMapPage(this,new RoutedEventArgs());c.Hud.SetForm("panel");
    }
}

