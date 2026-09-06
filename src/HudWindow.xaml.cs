using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace WarDogs;
public partial class HudWindow:Window
{
    readonly Controller c;public string Form{get;private set;}="panel";string backForm="panel";
    TextBlock? distance,mil,azimuth,status,notice,positions;Button? mode,map,weapon,pause;Border? shell;
    readonly Brush ink=Brush("#F1F4FA"),muted=Brush("#99A4B7"),blue=Brush("#91BFFD"),amber=Brush("#F1BE82");
    Point? dragStart;double startLeft,startTop;bool moved;
    static Brush Brush(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
    public HudWindow(Controller control)
    {
        c=control;InitializeComponent();c.Updated+=Render;
        Left=double.IsFinite(c.Pref.HudLeft)?c.Pref.HudLeft:SystemParameters.WorkArea.Right-370;
        Top=double.IsFinite(c.Pref.HudTop)?c.Pref.HudTop:80;
        SetForm(c.Pref.HudForm is "compact" or "bubble"?c.Pref.HudForm:"panel");
    }
    TextBlock Text(string value,double size=12,Brush? color=null)=>new(){Text=value,FontSize=size,Foreground=color??ink,VerticalAlignment=VerticalAlignment.Center};
    Button Btn(string text,Action action,string hint="",bool primary=false,bool tiny=false)
    {
        var b=new Button{Content=text,Padding=tiny?new Thickness(5,2,5,2):new Thickness(9,6,9,6),Margin=new Thickness(0,0,tiny?3:5,0),FontSize=tiny?10:12,ToolTip=hint.Length>0?hint:text,Background=primary?Brush("#2D507C"):Brush(tiny?"#B31A202B":"#252D3B")};
        b.Click+=(s,e)=>action();return b;
    }
    public void SetForm(string form)
    {
        if(form=="settings"&&Form!="settings")backForm=Form=="bubble"?"panel":Form;
        Form=form;distance=mil=azimuth=status=notice=positions=null;mode=map=weapon=pause=null;shell=null;
        c.IsRecordingHotkey=false;Surface.Children.Clear();Surface.LayoutTransform=new ScaleTransform(Math.Clamp(c.Pref.HudScale,.75,1.5),Math.Clamp(c.Pref.HudScale,.75,1.5));Opacity=1;
        if(form=="bubble")BuildBubble();else if(form=="compact")BuildCompact();else if(form=="settings")BuildSettings();else BuildPanel();
        if(form!="settings")c.Pref.HudForm=form;
        Render();UpdateLayout();ClampPosition();c.Save();
    }
    void ClampPosition(){Left=Math.Clamp(Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Math.Max(44,ActualWidth));Top=Math.Clamp(Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Math.Max(44,ActualHeight));}
    StackPanel Card(double width)
    {
        var p=new StackPanel();shell=new Border{Width=width,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=Brush("#39465B"),Background=Brush("#F2141923"),Child=p};Surface.Children.Add(shell);return p;
    }
    Grid Header(string title,params Button[] buttons)
    {
        var g=new Grid{Margin=new Thickness(0,0,0,10),Background=Brush("#01141923"),Cursor=Cursors.SizeAll};
        g.ColumnDefinitions.Add(new());g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var caption=Text("⠿  "+title,11,muted);caption.ToolTip="按住标题或空白区域拖动";g.Children.Add(caption);
        var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var b in buttons)row.Children.Add(b);Grid.SetColumn(row,1);g.Children.Add(row);return g;
    }
    void Metrics(Panel parent,bool compact)
    {
        var g=new Grid{Margin=new Thickness(0,compact?0:10,0,compact?0:9)};
        foreach(var v in new[]{1d,1.25,1d})g.ColumnDefinitions.Add(new(){Width=new GridLength(v,GridUnitType.Star)});
        TextBlock Metric(int col,string label,Brush color){var p=new StackPanel();p.Children.Add(Text(label,9,muted));var n=Text("—",compact?27:30,color);n.FontWeight=FontWeights.SemiBold;
            if(compact)p.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1};p.Children.Add(n);Grid.SetColumn(p,col);g.Children.Add(p);return n;}
        distance=Metric(0,"距离 m",ink);mil=Metric(1,"密位 MIL",blue);azimuth=Metric(2,"方位 °",ink);parent.Children.Add(g);
    }
    void Selectors(Panel parent)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        weapon=Btn("",()=>c.Act("weapon"),"切换炮种：迫击炮 / SPH-2");map=Btn("",()=>c.Act("map"),"切换地图：Bakurani / Ozeti");mode=Btn("",()=>c.Act("mode"),"切换输入模式");
        row.Children.Add(weapon);row.Children.Add(map);row.Children.Add(mode);parent.Children.Add(row);
    }
    void BuildPanel()
    {
        var p=Card(348);p.Children.Add(Header("火力面板",Btn("透明",()=>SetForm("compact"),"只保留数字与微型按键",tiny:true),Btn("⚙",()=>SetForm("settings"),"HUD 内设置，无需打开地图",tiny:true),Btn("●",()=>SetForm("bubble"),"收为小圆球",tiny:true)));
        Selectors(p);Metrics(p,false);
        var actions=new UniformGrid{Columns=3};actions.Children.Add(Btn("设炮位",()=>c.Act("origin"),"按当前模式读取或等待炮位"));actions.Children.Add(Btn("选目标",()=>c.Act("target"),"按当前模式读取或等待目标",true));pause=Btn("暂停",()=>c.Act("pause"));actions.Children.Add(pause);p.Children.Add(actions);
        status=Text("",11,blue);status.Margin=new Thickness(0,9,0,0);status.TextTrimming=TextTrimming.CharacterEllipsis;p.Children.Add(status);
        notice=Text("",10,muted);notice.TextTrimming=TextTrimming.CharacterEllipsis;notice.Margin=new Thickness(0,5,0,0);p.Children.Add(notice);
    }
    void BuildCompact()
    {
        var p=new StackPanel{Width=304,Margin=new Thickness(5,3,5,3)};Surface.Children.Add(p);Metrics(p,true);
        var row=new StackPanel{Orientation=Orientation.Horizontal};status=Text("●",10,blue);status.Width=13;row.Children.Add(status);
        foreach(var (label,action,hint) in new[]{("炮","origin","设置炮位"),("靶","target","选定目标"),("Ⅱ","pause","停止 / 恢复")})row.Children.Add(Btn(label,()=>c.Act(action),hint,tiny:true));
        weapon=Btn("",()=>c.Act("weapon"),"切换炮种",tiny:true);map=Btn("",()=>c.Act("map"),"切换地图",tiny:true);mode=Btn("",()=>c.Act("mode"),"切换模式",tiny:true);
        row.Children.Add(weapon);row.Children.Add(map);row.Children.Add(mode);row.Children.Add(Btn("⚙",()=>SetForm("settings"),"设置",tiny:true));row.Children.Add(Btn("▣",()=>SetForm("panel"),"展开面板",tiny:true));row.Children.Add(Btn("●",()=>SetForm("bubble"),"收为圆球",tiny:true));p.Children.Add(row);
        p.ToolTip="按住数字拖动 · 蓝色就绪 / 橙色等待 / 灰色暂停";
    }
    void BuildBubble()
    {
        status=Text("●",18,blue);status.HorizontalAlignment=HorizontalAlignment.Center;
        Surface.Children.Add(new Border{Width=40,Height=40,CornerRadius=new CornerRadius(20),Background=Brush("#CE141923"),BorderBrush=Brush("#52637D"),BorderThickness=new Thickness(1),Child=status,Cursor=Cursors.Hand,ToolTip="单击展开 · 拖动移动"});
    }
    void BuildSettings()
    {
        var p=Card(370);p.Children.Add(Header("HUD 设置",Btn("返回",()=>SetForm(backForm),tiny:true),Btn("●",()=>SetForm("bubble"),"收为圆球",tiny:true)));
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};var body=new Grid();
        var config=new SettingsPanel(c,true);var manual=new StackPanel();positions=Text("",12,muted);positions.LineHeight=23;manual.Children.Add(positions);
        var input=new TextBox{Margin=new Thickness(0,12,0,8),ToolTip="x12.11 y11.11，可带尾随文字"};manual.Children.Add(input);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(Btn("设为炮位",()=>c.Manual(input.Text,true)));buttons.Children.Add(Btn("设为目标",()=>c.Manual(input.Text,false),primary:true));manual.Children.Add(buttons);
        var mapButton=Btn("打开大型地图",()=>c.ShowMap());mapButton.Margin=new Thickness(0,12,0,8);manual.Children.Add(mapButton);manual.Children.Add(Btn("退出应用",()=>c.Quit()));
        var history=Text(string.Join("\n",c.Notices.Take(12)),11,muted);history.TextWrapping=TextWrapping.Wrap;history.LineHeight=22;
        void Tab(UIElement child){c.IsRecordingHotkey=false;body.Children.Clear();body.Children.Add(child);}
        tabs.Children.Add(Btn("快捷键 / 外观",()=>Tab(config)));tabs.Children.Add(Btn("坐标 / 工具",()=>Tab(manual)));tabs.Children.Add(Btn("近期提示",()=>Tab(history)));p.Children.Add(tabs);Tab(config);
        p.Children.Add(new ScrollViewer{Content=body,MaxHeight=Math.Min(450,SystemParameters.WorkArea.Height-160),VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        notice=Text("",10,blue);notice.TextWrapping=TextWrapping.Wrap;notice.Margin=new Thickness(0,10,0,0);p.Children.Add(notice);
    }
    void Render()
    {
        var s=c.State;var r=c.Result;var color=s.Paused?muted:s.Waiting!=Awaiting.None||r is {InRange:false}?amber:blue;
        if(distance!=null)distance.Text=r?.Distance.ToString("0")??"—";if(azimuth!=null)azimuth.Text=r?.Azimuth?.ToString("0.0")??"—";
        if(mil!=null){mil.Text=r==null?"—":s.Weapon=="mortar"?r.Single?.ToString()??"—":$"{r.Low?.ToString()??"—"}/{r.High?.ToString()??"—"}";mil.FontSize=s.Weapon=="mortar"?(Form=="compact"?27:30):17;mil.Foreground=r is {InRange:false}?amber:blue;mil.ToolTip=s.Weapon=="spg"?"低抛 / 高抛":"迫击炮仰角";}
        if(status!=null){status.Text=Form is "compact" or "bubble"?s.Paused?"Ⅱ":s.Waiting==Awaiting.Origin?"炮":s.Waiting==Awaiting.Target?"靶":"●":$"● {s.Status}  ·  {r?.Status??"等待坐标"}";status.Foreground=color;status.ToolTip=$"{s.Status} · {r?.Status??"等待坐标"}\n{c.Notices.FirstOrDefault()}";}
        if(weapon!=null)weapon.Content=Form=="compact"?(s.Weapon=="mortar"?"迫":"SPH"):(s.Weapon=="mortar"?"迫击炮 ↻":"SPH-2 ↻");
        if(map!=null)map.Content=Form=="compact"?(s.Map=="bakurani"?"B":"O"):(s.Map=="bakurani"?"Bakurani ↻":"Ozeti ↻");
        if(mode!=null)mode.Content=Form=="compact"?s.ModeText[..1]:s.ModeText+" ↻";if(pause!=null)pause.Content=s.Paused?"恢复":"暂停";
        if(notice!=null)notice.Text=c.Notices.FirstOrDefault()??"";
        if(positions!=null)positions.Text=$"炮位  {s.Current.Origin?.ToString()??"未设置"}\n目标  {s.Current.Target?.ToString()??"未设置"}\n来源  {s.Current.Source}";
        if(shell!=null)shell.Background=new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(c.Pref.HudOpacity,.25,1)*255),20,25,35));
        var scale=Math.Clamp(c.Pref.HudScale,.75,1.5);if(Surface.LayoutTransform is ScaleTransform t&&t.ScaleX!=scale){Surface.LayoutTransform=new ScaleTransform(scale,scale);UpdateLayout();ClampPosition();}
    }
    bool IsControl(DependencyObject? node)
    {
        while(node!=null&&node!=Surface){if(node is ButtonBase or TextBoxBase or Slider or ScrollBar or ComboBox)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;
    }
    void PointerDown(object sender,MouseButtonEventArgs e)
    {
        if(IsControl(e.OriginalSource as DependencyObject))return;dragStart=PointToScreen(e.GetPosition(this));startLeft=Left;startTop=Top;moved=false;Surface.CaptureMouse();e.Handled=true;
    }
    public void MoveByScreenDelta(double dx,double dy){var dpi=VisualTreeHelper.GetDpi(this);Left=startLeft+dx/dpi.DpiScaleX;Top=startTop+dy/dpi.DpiScaleY;}
    void PointerMove(object sender,MouseEventArgs e)
    {
        if(dragStart is not {} start||e.LeftButton!=MouseButtonState.Pressed)return;var now=PointToScreen(e.GetPosition(this));var d=now-start;if(d.Length>3)moved=true;if(moved)MoveByScreenDelta(d.X,d.Y);
    }
    void PointerUp(object sender,MouseButtonEventArgs e)
    {
        if(dragStart==null)return;bool expand=Form=="bubble"&&!moved;dragStart=null;Surface.ReleaseMouseCapture();if(expand)SetForm("compact");ClampPosition();c.Save();e.Handled=true;
    }
    void CaptureLost(object s,MouseEventArgs e){dragStart=null;}
}
