using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Data;
using System.Runtime.InteropServices;
namespace WarDogs;
public partial class HudWindow:Window
{
    readonly Controller c;public string Form{get;private set;}="panel";string backForm="panel";
    TextBlock? distance,mil,azimuth,status,notice,positions;Button? mode,map,weapon,pause,origin,target,latest;Border? shell;
    Expander? history;TextBlock? hiddenKey;Button? settingsButton;
    System.Windows.Shapes.Ellipse? bubbleDot; TextBlock? originReadout,targetReadout,bubbleReadout;Border? originBack,targetBack,quickInputBack;TextBox? quickInput;Button? quickConfirm;
    readonly List<(SolidColorBrush Brush,bool Button)> backgrounds=new();
    string settingsTab="config";
    readonly Brush ink=Brush("#F5F7FB"),muted=Brush("#D0DAE8"),blue=Brush("#91C5FF"),amber=Brush("#FFBD87");
    bool quickOrigin;
    Point? dragStart;double startLeft,startTop;bool moved;
    static Brush Brush(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
    public HudWindow(Controller control)
    {
        c=control;InitializeComponent();c.Updated+=Render;Surface.ContextMenuOpening+=(s,e)=>{Surface.ContextMenu=BubbleMenu();};
        Left=double.IsFinite(c.Pref.HudLeft)?c.Pref.HudLeft:SystemParameters.WorkArea.Right-370;
        Top=double.IsFinite(c.Pref.HudTop)?c.Pref.HudTop:80;
        SetForm(c.Pref.HudForm is "compact" or "bubble"?c.Pref.HudForm:"panel");
    }
    TextBlock Text(string value,double size=12,Brush? color=null)=>new(){Text=value,FontSize=size,Foreground=color??ink,VerticalAlignment=VerticalAlignment.Center,Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=2,ShadowDepth=1,Opacity=.9}};
    SolidColorBrush LayerBrush(string hex,bool button=false){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));backgrounds.Add((brush,button));return brush;}
    Border Back(UIElement child)=>new(){Background=LayerBrush("#141923"),CornerRadius=new CornerRadius(4),Padding=new Thickness(5,3,5,3),Child=child};
    Button Btn(string text,Action action,string hint="",bool primary=false,bool tiny=false,Action? doubleAction=null)
    {
        var b=new Button{Content=text,Padding=tiny?new Thickness(6,4,6,4):new Thickness(7,5,7,5),Margin=new Thickness(0,0,4,0),FontSize=tiny?11:12,ToolTip=hint.Length>0?hint:text,Background=LayerBrush(primary?"#315781":"#232E40",true),BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1)};
        var border=new FrameworkElementFactory(typeof(Border),"ButtonShell");border.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=RelativeSource.TemplatedParent});border.SetBinding(Border.PaddingProperty,new Binding("Padding"){RelativeSource=RelativeSource.TemplatedParent});border.SetBinding(Border.BorderThicknessProperty,new Binding("BorderThickness"){RelativeSource=RelativeSource.TemplatedParent});border.SetBinding(Border.BorderBrushProperty,new Binding("BorderBrush"){RelativeSource=RelativeSource.TemplatedParent});border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetBinding(ContentPresenter.ContentProperty,new Binding("Content"){RelativeSource=RelativeSource.TemplatedParent});content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,blue,"ButtonShell"));template.Triggers.Add(hover);b.Template=template;
        ToolTipService.SetInitialShowDelay(b,250);ToolTipService.SetShowDuration(b,30000);
        if(doubleAction==null)b.Click+=(s,e)=>action();
        else
        {
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime())};
            timer.Tick+=(s,e)=>{timer.Stop();action();};
            b.Click+=(s,e)=>{timer.Stop();timer.Start();};
            b.PreviewMouseLeftButtonDown+=(s,e)=>{if(e.ClickCount<2)return;timer.Stop();e.Handled=true;doubleAction();};
            b.Unloaded+=(s,e)=>timer.Stop();
        }
        return b;
    }
    void ActionLabel(Button? button,string label,string action,string hint)
    {
        if(button==null)return;var key=c.Pref.Keys.GetValueOrDefault(action,"");var stamp=label+"|"+key;
        if((string?)button.Tag!=stamp){var stack=new StackPanel();var text=Text(label,Form=="compact"?11:12);text.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(text);
            if(key.Length>0&&Form!="compact"){var keys=Text(key,9,muted);keys.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(keys);}button.Content=stack;button.Tag=stamp;}
        button.ToolTip=hint+(key.Length>0?"\n快捷键："+key:"\n未设置快捷键");
    }
    public void SetForm(string form)
    {
        if(form=="settings"&&Form!="settings")backForm=Form;
        Form=form;distance=mil=azimuth=status=notice=positions=hiddenKey=null;mode=map=weapon=pause=origin=target=latest=settingsButton=null;history=null;shell=null;
        bubbleDot=null;originReadout=targetReadout=bubbleReadout=null;originBack=targetBack=quickInputBack=null;quickInput=null;quickConfirm=null;backgrounds.Clear();Surface.ContextMenu=null;
        c.IsRecordingHotkey=false;Surface.Children.Clear();Surface.LayoutTransform=new ScaleTransform(Math.Clamp(c.Pref.HudScale,.75,1.5),Math.Clamp(c.Pref.HudScale,.75,1.5));Opacity=1;
        if(form=="bubble")BuildBubble();else if(form=="compact")BuildCompact();else if(form=="settings")BuildSettings();else BuildPanel();
        if(form!="settings")c.Pref.HudForm=form;
        Surface.ContextMenu=BubbleMenu();Render();UpdateLayout();ClampPosition();c.Save();
    }
    void ClampPosition(){Left=Math.Clamp(Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Math.Max(44,ActualWidth));Top=Math.Clamp(Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Math.Max(44,ActualHeight));}
    StackPanel Card(double width)
    {
        var p=new StackPanel();shell=new Border{Width=width,Padding=new Thickness(10),CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=Brush("#536781"),Child=p};Surface.Children.Add(shell);return p;
    }
    Grid Header(bool compact)
    {
        var g=new Grid{Margin=new Thickness(0,0,0,7),Cursor=Cursors.SizeAll};g.ColumnDefinitions.Add(new());g.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var caption=Back(Text("⠿ HUD",10,muted));caption.HorizontalAlignment=HorizontalAlignment.Left;caption.ToolTip="按住标题、数字或空白区域拖动";g.Children.Add(caption);
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        row.Children.Add(Btn("大地图",()=>c.ShowMap(),"打开桌面战术大地图",tiny:true));
        settingsButton=Btn("设置",()=>SetForm("settings"),"软件更新、快捷键、外观、手动坐标与记录",tiny:true);row.Children.Add(settingsButton);
        row.Children.Add(Btn("点化",()=>SetForm("bubble"),"收为小圆球 · 点击圆球还原简化界面",tiny:true));
        var toggle=Btn(compact?"还原界面":"简化",()=>SetForm(compact?"panel":"compact"),compact?"还原正常悬浮窗":"切换为简化悬浮窗",true,true);toggle.Width=76;toggle.Margin=new Thickness(0);toggle.FontWeight=FontWeights.Bold;
        row.Children.Add(toggle);Grid.SetColumn(row,1);g.Children.Add(row);return g;
    }
    void Metrics(Panel parent,bool compact)
    {
        var g=new Grid{Margin=new Thickness(0,0,0,6)};foreach(var v in new[]{1d,1.2,1d})g.ColumnDefinitions.Add(new(){Width=new GridLength(v,GridUnitType.Star)});
        TextBlock Metric(int col,string label,Brush color){var p=new StackPanel();p.Children.Add(Text(label,10,muted));var n=Text("—",compact?27:30,color);n.FontWeight=FontWeights.SemiBold;p.Children.Add(n);var tile=Back(p);tile.Margin=new Thickness(0,0,col==2?0:4,0);Grid.SetColumn(tile,col);g.Children.Add(tile);return n;}
        distance=Metric(0,"距离 m",ink);mil=Metric(1,"密位 MIL",blue);azimuth=Metric(2,"方位 °",ink);parent.Children.Add(g);
    }
    void Selectors(Panel parent)
    {
        var row=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,6)};
        weapon=Btn("",()=>c.Act("weapon"),"切换炮种：迫击炮 / SPH-2",tiny:true);map=Btn("",()=>c.Act("map"),tiny:true);mode=Btn("",()=>c.Act("mode"),tiny:true);
        row.Children.Add(weapon);row.Children.Add(map);row.Children.Add(mode);mode.Margin=new Thickness(0);parent.Children.Add(row);
    }
    void Actions(Panel parent)
    {
        var row=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,6)};origin=Btn("",()=>c.Act("origin"),doubleAction:()=>OpenCoordinateInput(true));target=Btn("",()=>c.Act("target"),primary:true,doubleAction:()=>OpenCoordinateInput(false));pause=Btn("",()=>c.Act("pause"));pause.Margin=new Thickness(0);
        row.Children.Add(origin);row.Children.Add(target);row.Children.Add(pause);parent.Children.Add(row);
    }
    void BuildPanel()
    {
        var p=Card(348);p.Children.Add(Header(false));Selectors(p);Metrics(p,false);CoordinateLine(p);Actions(p);
        status=Text("",11,blue);status.TextTrimming=TextTrimming.CharacterEllipsis;p.Children.Add(Back(status));
        notice=Text("",10,muted);notice.TextWrapping=TextWrapping.Wrap;
        latest=Btn("",()=>{if(c.History.FirstOrDefault() is {} h)c.RestoreHistory(h);});latest.Content=notice;latest.Margin=new Thickness(0,5,0,0);latest.HorizontalContentAlignment=HorizontalAlignment.Left;p.Children.Add(latest);
        history=new Expander{Header="展开本次记录 ▾",FontSize=11,Foreground=ink,Background=Brush("#F0141923"),Margin=new Thickness(0,4,0,0),Content=new HistoryPanel(c){Height=230}};
        history.Expanded+=(s,e)=>{UpdateLayout();ClampPosition();};p.Children.Add(history);
        hiddenKey=Text("",9,muted);hiddenKey.Margin=new Thickness(0,4,0,0);p.Children.Add(Back(hiddenKey));
    }
    void BuildCompact()
    {
        var p=new StackPanel{Width=326,Margin=new Thickness(11)};Surface.Children.Add(p);p.Children.Add(Header(true));Metrics(p,true);CoordinateLine(p);
        var row=new UniformGrid{Columns=6,Margin=new Thickness(0,0,0,6)};
        origin=Btn("",()=>c.Act("origin"),tiny:true,doubleAction:()=>OpenCoordinateInput(true));target=Btn("",()=>c.Act("target"),primary:true,tiny:true,doubleAction:()=>OpenCoordinateInput(false));pause=Btn("",()=>c.Act("pause"),tiny:true);
        weapon=Btn("",()=>c.Act("weapon"),"切换迫击炮 / SPH-2",tiny:true);map=Btn("",()=>c.Act("map"),tiny:true);mode=Btn("",()=>c.Act("mode"),tiny:true);
        foreach(var button in new[]{origin,target,pause,weapon,map,mode}){button.Padding=new Thickness(2,4,2,4);button.Margin=new Thickness(0,0,3,0);row.Children.Add(button);}mode.Margin=new Thickness(0);p.Children.Add(row);
        status=Text("",10,blue);status.TextTrimming=TextTrimming.CharacterEllipsis;p.Children.Add(Back(status));
        p.ToolTip="按住数字拖动 · 蓝色就绪 / 橙色等待 / 灰色暂停";
    }
    void BuildBubble()
    {
        status=Text("",10,blue);status.HorizontalAlignment=HorizontalAlignment.Center;status.TextAlignment=TextAlignment.Center;status.Effect=null; bubbleDot=new System.Windows.Shapes.Ellipse{Width=6,Height=6,Fill=blue,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}; var face=new Grid();face.Children.Add(bubbleDot);face.Children.Add(status);
        var row=new StackPanel{Orientation=Orientation.Horizontal};Surface.Children.Add(row);
        row.Children.Add(new Border{Width=20,Height=20,CornerRadius=new CornerRadius(10),VerticalAlignment=VerticalAlignment.Center,Background=Brush("#F0141923"),BorderBrush=Brush("#52637D"),BorderThickness=new Thickness(1),Child=face,Cursor=Cursors.Hand,ToolTip="单击展开 · 拖动移动 · 右键操作菜单"});
        bubbleReadout=Text("",18,blue);bubbleReadout.Margin=new Thickness(8,0,0,0);row.Children.Add(bubbleReadout);
        Surface.ContextMenu=BubbleMenu();
    }
    static string TowerSuffix(string proximity)=>string.IsNullOrEmpty(proximity)||proximity=="200 m 内无 Tower"?"":" · "+proximity;
    void CoordinateLine(Panel panel)
    {
        targetReadout=Text("",11,amber);targetReadout.TextWrapping=TextWrapping.Wrap;targetBack=Back(targetReadout);targetBack.Margin=new Thickness(0,0,0,4);panel.Children.Add(targetBack);
        originReadout=Text("",11,blue);originReadout.TextWrapping=TextWrapping.Wrap;originBack=Back(originReadout);originBack.Margin=new Thickness(0,0,0,6);panel.Children.Add(originBack);

        var inputRow=new Grid();inputRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});inputRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        quickInput=new TextBox{Padding=new Thickness(5,4,5,4),Margin=new Thickness(0,0,5,0),ToolTip="粘贴 x12.11 y11.11，按回车确认"};quickInput.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){SubmitCoordinateInput();e.Handled=true;}else if(e.Key==Key.Escape){CloseCoordinateInput();e.Handled=true;}};inputRow.Children.Add(quickInput);
        quickConfirm=Btn("确认",SubmitCoordinateInput,primary:true,tiny:true);quickConfirm.Margin=new Thickness(0);Grid.SetColumn(quickConfirm,1);inputRow.Children.Add(quickConfirm);
        quickInputBack=Back(inputRow);quickInputBack.Margin=new Thickness(0,0,0,6);quickInputBack.Visibility=Visibility.Collapsed;panel.Children.Add(quickInputBack);
    }
    void OpenCoordinateInput(bool originMode)
    {
        if(quickInput==null||quickInputBack==null||quickConfirm==null)return;quickOrigin=originMode;quickInput.Text="";quickInput.ToolTip=originMode?"粘贴炮位坐标，按回车确认":"粘贴目标坐标，按回车确认";quickConfirm.Content=originMode?"设炮位":"设目标";quickInputBack.Visibility=Visibility.Visible;UpdateLayout();ClampPosition();Dispatcher.BeginInvoke(()=>{quickInput.Focus();Keyboard.Focus(quickInput);});
    }
    void SubmitCoordinateInput()
    {
        if(quickInput==null)return;if(Coordinates.Parse(quickInput.Text,true)==null){c.Manual(quickInput.Text,quickOrigin);quickInput.SelectAll();return;}c.Manual(quickInput.Text,quickOrigin);CloseCoordinateInput();
    }
    void CloseCoordinateInput(){if(quickInputBack==null)return;quickInputBack.Visibility=Visibility.Collapsed;Keyboard.ClearFocus();UpdateLayout();ClampPosition();}
    public void ShowForm(string form){SetForm(form);Show();Activate();} public void OpenSettings(string tab="config"){settingsTab=tab;ShowForm("settings");}
    public ContextMenu BubbleMenu()
    {
        var menu=new ContextMenu{Background=Brush("#F3F3F3"),Foreground=Brushes.Black};
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,Brushes.Black));menu.Resources.Add(typeof(TextBlock),textStyle);
        var headerText=new FrameworkElementFactory(typeof(TextBlock));headerText.SetBinding(TextBlock.TextProperty,new Binding());headerText.SetValue(TextBlock.ForegroundProperty,Brushes.Black);var headerTemplate=new DataTemplate{VisualTree=headerText};
        MenuItem Item(string label,Action action,string? key=null){var item=new MenuItem{Header=label+(string.IsNullOrEmpty(key)?"":"    "+key),HeaderTemplate=headerTemplate,Foreground=Brushes.Black};item.Click+=(s,e)=>{e.Handled=true;action();};return item;}
        void Add(string label,Action action,string? key=null)=>menu.Items.Add(Item(label,action,key));
        Add("打开大地图",()=>c.ShowMap());var settingsItem=Item("设置",()=>OpenSettings());settingsItem.Header=UpdatePanel.Badge("设置",c.Updates.HasUpdate);settingsItem.HeaderTemplate=null;menu.Items.Add(settingsItem);Add("还原界面",()=>ShowForm("panel"));Add("简化界面",()=>ShowForm("compact"));Add("小球模式",()=>ShowForm("bubble"));
        var minimal=Item("小球极简化 · 右侧纯文字读数",()=>{c.Pref.BubbleReadout=!c.Pref.BubbleReadout;c.Refresh();c.Save();});minimal.IsCheckable=true;minimal.IsChecked=c.Pref.BubbleReadout;menu.Items.Add(minimal);menu.Items.Add(new Separator());
        foreach(var (id,label) in new[]{("origin","设置炮位 / 取消等待"),("target","选定目标"),("pause",c.State.Paused?"恢复接收":"暂停接收"),("hud",IsVisible?"隐藏 HUD":"显示 HUD")})Add(label,()=>c.Act(id),c.Pref.Keys.GetValueOrDefault(id));
        var modeMenu=new MenuItem{Header="输入模式",HeaderTemplate=headerTemplate};foreach(var value in Enum.GetValues<InputMode>()){var item=Item(value==InputMode.Smart?"智能模式":value==InputMode.Manual?"精确手动":"连续目标",()=>{while(c.State.Mode!=value)c.Act("mode");});item.IsCheckable=true;item.IsChecked=c.State.Mode==value;modeMenu.Items.Add(item);}menu.Items.Add(modeMenu);
        var mapMenu=new MenuItem{Header="地图",HeaderTemplate=headerTemplate};foreach(var (id,label) in new[]{("bakurani","Bakurani"),("ozeti","Ozeti")}){var item=Item(label,()=>{if(c.State.Map!=id)c.Act("map");});item.IsCheckable=true;item.IsChecked=c.State.Map==id;mapMenu.Items.Add(item);}menu.Items.Add(mapMenu);
        var weaponMenu=new MenuItem{Header="炮种",HeaderTemplate=headerTemplate};foreach(var (id,label) in new[]{("mortar","迫击炮"),("spg","SPH-2")}){var item=Item(label,()=>{if(c.State.Weapon!=id)c.Act("weapon");});item.IsCheckable=true;item.IsChecked=c.State.Weapon==id;weaponMenu.Items.Add(item);}menu.Items.Add(weaponMenu);
        menu.Items.Add(new Separator());Add("手动输入坐标",()=>OpenSettings("tools"));Add("本次记录 / 恢复坐标",()=>OpenSettings("history"));
        Add("联机房间 / 成员列表",()=>OpenSettings("room"));Add("发布任务 / 取消等待",()=>c.Act("publishTask"),c.Pref.Keys.GetValueOrDefault("publishTask"));Add("选择房间任务",()=>c.Act("roomTasks"),c.Pref.Keys.GetValueOrDefault("roomTasks"));
        if(c.Pending!=null){Add("采用暂存坐标",()=>c.Act("pending"));Add("忽略暂存坐标",()=>c.Act("discard"));}
        Add("退出应用",()=>c.Quit());return menu;
    }
    Brush BubbleColor(){try{return Brush(c.Pref.BubbleTextColor);}catch{return blue;}}
    public void ExpandHistory(){if(history!=null)history.IsExpanded=true;}
    void BuildSettings()
    {
        var p=Card(390);var top=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
        top.Children.Add(Btn("返回界面",()=>SetForm(backForm),primary:true));top.Children.Add(Btn("大地图",()=>c.ShowMap()));top.Children.Add(Btn("点化",()=>SetForm("bubble")));p.Children.Add(top);
        var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};var body=new Grid();
        var config=new SettingsPanel(c,true);var configScroll=new ScrollViewer{Content=config,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=420};var manual=new StackPanel();positions=Text("",12,muted);positions.LineHeight=23;manual.Children.Add(Back(positions));
        var input=new TextBox{Margin=new Thickness(0,12,0,8),ToolTip="x12.11 y11.11，可带尾随文字"};manual.Children.Add(input);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(Btn("设为炮位",()=>c.Manual(input.Text,true)));buttons.Children.Add(Btn("设为目标",()=>c.Manual(input.Text,false),primary:true));manual.Children.Add(buttons);
        var exit=Btn("退出应用",()=>c.Quit());exit.Margin=new Thickness(0,12,0,0);manual.Children.Add(exit);
        void Tab(UIElement child){c.IsRecordingHotkey=false;body.Children.Clear();body.Children.Add(child);}
        tabs.Children.Add(Btn("快捷键 / 外观",()=>Tab(configScroll)));tabs.Children.Add(Btn("坐标 / 工具",()=>Tab(manual)));tabs.Children.Add(Btn("本次记录",()=>Tab(new HistoryPanel(c){Height=420})));tabs.Children.Add(Btn("联机",()=>Tab(new ScrollViewer{Content=new WarDogs.Multiplayer.RoomSettingsPanel(c),MaxHeight=420,VerticalScrollBarVisibility=ScrollBarVisibility.Auto})));p.Children.Add(tabs);
        Tab(settingsTab=="history"?new HistoryPanel(c){Height=420}:settingsTab=="tools"?manual:settingsTab=="room"?new ScrollViewer{Content=new WarDogs.Multiplayer.RoomSettingsPanel(c),MaxHeight=420,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}:configScroll);settingsTab="config";p.Children.Add(body);
        notice=Text("",10,blue);notice.TextWrapping=TextWrapping.Wrap;var footer=Back(notice);footer.Margin=new Thickness(0,10,0,0);p.Children.Add(footer);
    }
    void Render()
    {
        if(settingsButton!=null)settingsButton.Content=UpdatePanel.Badge("设置",c.Updates.HasUpdate);
        var s=c.State;var r=c.Result;var color=s.Paused?muted:s.Waiting!=Awaiting.None||r is {InRange:false}?amber:blue;
        if(distance!=null)distance.Text=r?.Distance.ToString("0")??"—";if(azimuth!=null)azimuth.Text=r?.Azimuth?.ToString("0.0")??"—";
        if(mil!=null){mil.Text=r==null?"—":s.Weapon=="mortar"?r.Single?.ToString()??"—":$"{r.Low?.ToString()??"—"}/{r.High?.ToString()??"—"}";mil.FontSize=s.Weapon=="mortar"?(Form=="compact"?27:30):17;mil.Foreground=r is {InRange:false}?amber:blue;mil.ToolTip=s.Weapon=="spg"?"低抛 / 高抛":"迫击炮仰角";}
        if(status!=null){status.Text=Form=="bubble"?s.Paused?"Ⅱ":s.Waiting==Awaiting.Origin?"炮":s.Waiting==Awaiting.Target?"靶":"":$"● {s.Status} · {r?.Status??"等待坐标"}";status.Foreground=color;status.ToolTip=$"{s.Status} · {r?.Status??"等待坐标"}\n{c.Notices.FirstOrDefault()}";}
        if(bubbleDot!=null){bubbleDot.Fill=color;bubbleDot.Visibility=s.Paused||s.Waiting!=Awaiting.None?Visibility.Collapsed:Visibility.Visible;} if(weapon!=null)weapon.Content=(s.Weapon=="mortar"?"迫击炮":"SPH-2")+(Form=="compact"?"":" ↻");
        ActionLabel(map,Form=="compact"?(s.Map=="bakurani"?"B图":"O图"):(s.Map=="bakurani"?"Bakurani ↻":"Ozeti ↻"),"map","切换地图 · 当前 "+s.Map);ActionLabel(mode,Form=="compact"?(s.Mode==InputMode.Smart?"智能":s.Mode==InputMode.Manual?"手动":"连续"):s.ModeText+" ↻","mode","切换坐标接收模式");
        ActionLabel(origin,"设炮位","origin","单击读取炮位 / 等待新坐标；双击粘贴输入；等待时再单击取消");ActionLabel(target,"选目标","target","单击读取目标 / 等待新坐标；双击粘贴输入");ActionLabel(pause,s.Paused?"恢复":"暂停","pause","停止 / 恢复坐标接收");
        if(origin?.Background is SolidColorBrush originBrush)
        {
            var stateColor=(Color)ColorConverter.ConvertFromString(s.Waiting==Awaiting.Origin?"#A97822":"#232E40");
            originBrush.Color=Color.FromArgb(originBrush.Color.A,stateColor.R,stateColor.G,stateColor.B);origin.BorderBrush=Brushes.Transparent;origin.BorderThickness=new Thickness(1);
        }
        var entry=c.History.FirstOrDefault();if(notice!=null){notice.Text=Form=="settings"?entry?.FullText??"":entry==null?"本次暂无记录":entry.Title+TowerSuffix(entry.Proximity);notice.Foreground=entry==null?muted:Brush(entry.Color);}
        if(latest!=null)latest.ToolTip=entry?.Hint??"暂无记录";
        if(hiddenKey!=null){var key=c.Pref.Keys.GetValueOrDefault("hud","");hiddenKey.Text=key.Length>0?"隐藏 / 显示 HUD  "+key:"隐藏 / 显示 HUD：未绑定快捷键";}
        if(positions!=null)positions.Text=$"炮位  {s.Current.Origin?.ToString()??"未设置"}\n目标  {s.Current.Target?.ToString()??"未设置"}\n来源  {s.Current.Source}";
        if(originReadout!=null)originReadout.Text="炮位  "+(s.Current.Origin?.ToString()??"未设置");
        if(targetReadout!=null)targetReadout.Text="目标  "+(s.Current.Target?.ToString()??"未设置")+(s.Current.Target is {} targetCoord?TowerSuffix(TowerProximity.Describe(targetCoord,c.Towers[s.Map])):"");
        if(targetBack!=null)targetBack.Visibility=s.Current.Target==null?Visibility.Collapsed:Visibility.Visible;
        if(originBack!=null)originBack.Visibility=s.Current.Origin==null?Visibility.Collapsed:Visibility.Visible;
        if(bubbleReadout!=null){bubbleReadout.Foreground=BubbleColor();bubbleReadout.Visibility=c.Pref.BubbleReadout?Visibility.Visible:Visibility.Collapsed;bubbleReadout.Text=$"{r?.Distance.ToString("0")??"—"}(m)  {(s.Weapon=="mortar"?r?.Single?.ToString()??"—":$"{r?.Low?.ToString()??"—"}/{r?.High?.ToString()??"—"}")} MIL  {r?.Azimuth?.ToString("0.0")??"—"} 度";}
        foreach(var (brush,button) in backgrounds){var colorValue=brush.Color;colorValue.A=(byte)(255*(Form=="settings"?1:Math.Clamp(button?c.Pref.HudButtonOpacity:c.Pref.HudTileOpacity,0,1)));brush.Color=colorValue;}
        if(shell!=null)shell.Background=new SolidColorBrush(Color.FromArgb((byte)((Form=="settings"?1:Math.Clamp(c.Pref.HudOpacity,.25,1))*255),20,25,35));
        if(Form=="bubble"){UpdateLayout();ClampPosition();}
        var scale=Math.Clamp(c.Pref.HudScale,.75,1.5);if(Surface.LayoutTransform is ScaleTransform t&&t.ScaleX!=scale){Surface.LayoutTransform=new ScaleTransform(scale,scale);UpdateLayout();ClampPosition();}
    }
    bool IsControl(DependencyObject? node)
    {
        while(node!=null&&node!=Surface){if(node is ButtonBase or TextBoxBase or Slider or ScrollBar or ComboBox or ListBoxItem)return true;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return false;
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
    [DllImport("user32.dll")]static extern uint GetDoubleClickTime();
}
