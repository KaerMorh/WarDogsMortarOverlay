using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarDogs;

public enum PzhCalibrationMode { Tilt, Linear }

public class PzhCalibrationSettings
{
    public bool Enabled{get;set;}
    public bool NeedsReset{get;set;}
    public PzhCalibrationMode Mode{get;set;}
    public string Map{get;set;}="";
    public Coord? Origin{get;set;}
    public List<PzhShot> Shots{get;set;}=[];
    public PzhTilt? Tilt{get;set;}
    public List<PzhLinearShot> LinearShots{get;set;}=[];
    public PzhLinearOffset? Linear{get;set;}
    public DateTime? Updated{get;set;}
    public PzhCalibrationSettings Copy()=>new(){Enabled=Enabled,NeedsReset=NeedsReset,Mode=Mode,Map=Map,Origin=Origin,Shots=Shots.ToList(),Tilt=Tilt,LinearShots=LinearShots.ToList(),Linear=Linear,Updated=Updated};
}

public record PzhDisplaySolution(Solution Raw,PzhCorrected? Corrected,bool Active,string CalibrationStatus);

public class PzhCalibrationWindow:Window
{
    readonly Controller c;PzhCalibrationSettings draft;readonly ListBox samples=new();
    readonly TextBox azimuth=new(){Width=82},mil=new(){Width=82},aimCoordinate=new(){MinWidth=205},manual=new(){MinWidth=210};
    readonly CheckBox enabled=new(){Content="开启校准补偿",Foreground=Brushes.White};
    readonly ComboBox mode=new(){Width=180,Foreground=Brushes.Black};
    readonly StackPanel linearAim=new();readonly TextBlock parameterTitle=new(),impactTitle=new(),introDescription=new(),introUsage=new(),state=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue};
    bool dirty,allowClose,syncing;
    static readonly Brush Dark=(Brush)new BrushConverter().ConvertFromString("#111925")!;
    static readonly Brush Border=(Brush)new BrushConverter().ConvertFromString("#3B4960")!;

    public PzhCalibrationWindow(Controller controller)
    {
        c=controller;draft=c.Pref.Pzh.Copy();Title="PZH 校准";Width=560;Height=680;MinWidth=510;MinHeight=560;
        Background=(Brush)new BrushConverter().ConvertFromString("#10141C")!;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        Button B(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(12,7,12,7)};if(primary)b.SetResourceReference(StyleProperty,"Primary");b.Click+=(s,e)=>action();return b;}
        footer.Children.Add(B("保存",Save,true));footer.Children.Add(B("关闭",Close));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});

        var intro=new StackPanel();
        intro.Children.Add(new TextBlock{Text="这是做什么的？",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        introDescription.TextWrapping=TextWrapping.Wrap;introDescription.LineHeight=20;intro.Children.Add(introDescription);
        introUsage.TextWrapping=TextWrapping.Wrap;introUsage.Foreground=Brushes.LightSteelBlue;introUsage.Margin=new Thickness(0,7,0,0);intro.Children.Add(introUsage);
        panel.Children.Add(new Border{Child=intro,Background=(Brush)new BrushConverter().ConvertFromString("#1A2432")!,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#536781")!,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)});

        var modeRow=new StackPanel{Orientation=Orientation.Horizontal};modeRow.Children.Add(new TextBlock{Text="校准模式",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});mode.Items.Add("倾斜姿态补偿");mode.Items.Add("XY 线性补偿");mode.SelectedIndex=draft.Mode==PzhCalibrationMode.Linear?1:0;modeRow.Children.Add(mode);panel.Children.Add(modeRow);
        panel.Children.Add(new TextBlock{Text="车辆换位置后需要重新校准。炮位变化只会停用旧补偿，不会自动删除已有数据。",Foreground=(Brush)new BrushConverter().ConvertFromString("#FFBD87")!,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});
        enabled.IsChecked=draft.Enabled;enabled.Click+=(s,e)=>{draft.Enabled=enabled.IsChecked==true;Changed();};panel.Children.Add(enabled);
        state.Margin=new Thickness(0,8,0,12);panel.Children.Add(state);

        linearAim.Children.Add(new TextBlock{Text="瞄准点 A 坐标",FontSize=15,Margin=new Thickness(0,4,0,7)});
        var aimRow=new DockPanel();var currentTarget=B("带入当前目标 A",LoadCurrentTarget);DockPanel.SetDock(currentTarget,Dock.Right);aimRow.Children.Add(currentTarget);aimCoordinate.ToolTip="x101.71 y119.96";aimRow.Children.Add(aimCoordinate);linearAim.Children.Add(aimRow);
        linearAim.Children.Add(new TextBlock{Text="输入 A 坐标会实时换算下方方位/MIL；输入方位和 MIL 也会实时反算 A 坐标。",Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)});panel.Children.Add(linearAim);

        parameterTitle.FontSize=15;parameterTitle.Margin=new Thickness(0,4,0,7);panel.Children.Add(parameterTitle);
        var fields=new StackPanel{Orientation=Orientation.Horizontal};fields.Children.Add(new TextBlock{Text="方位°",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)});fields.Children.Add(azimuth);fields.Children.Add(new TextBlock{Text="高抛 MIL",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,6,0)});fields.Children.Add(mil);fields.Children.Add(B("带入当前原始解",LoadCurrent));panel.Children.Add(fields);
        impactTitle.FontSize=15;impactTitle.Margin=new Thickness(0,14,0,7);panel.Children.Add(impactTitle);
        var input=new DockPanel();var addManual=B("手动添加",AddManual,true);DockPanel.SetDock(addManual,Dock.Right);input.Children.Add(addManual);var addClipboard=B("从剪贴板添加",AddClipboard);DockPanel.SetDock(addClipboard,Dock.Right);input.Children.Add(addClipboard);manual.ToolTip="x101.48 y120.16";input.Children.Add(manual);panel.Children.Add(input);
        panel.Children.Add(new TextBlock{Text="可反复添加、删除校准点；列表会立即显示当前模式算出的补偿参数。",Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)});

        samples.Height=215;samples.Background=Dark;samples.Foreground=Brushes.WhiteSmoke;samples.BorderBrush=Border;samples.BorderThickness=new Thickness(1);samples.Padding=new Thickness(4);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.WhiteSmoke));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(7,6,7,6)));var selected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFromString("#315781")!));itemStyle.Triggers.Add(selected);samples.ItemContainerStyle=itemStyle;panel.Children.Add(samples);
        var edits=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};edits.Children.Add(B("删除选中",RemoveSelected));edits.Children.Add(B("重置全部",Reset));panel.Children.Add(edits);

        azimuth.TextChanged+=(s,e)=>SyncAimFromAngles();mil.TextChanged+=(s,e)=>SyncAimFromAngles();aimCoordinate.TextChanged+=(s,e)=>SyncAnglesFromAim();mode.SelectionChanged+=(s,e)=>ChangeMode();
        Closing+=OnClosing;c.Updated+=ControllerUpdated;LoadCurrent();Render();
    }
    void ControllerUpdated()=>Dispatcher.BeginInvoke(()=>
    {
        if(draft.Origin!=null&&!draft.NeedsReset&&(draft.Map!=c.State.Map||draft.Origin!=c.State.Current.Origin)){draft.NeedsReset=true;dirty=true;}
        Render();
    });
    bool SameOrigin()=>!draft.NeedsReset&&draft.Map==c.State.Map&&draft.Origin==c.State.Current.Origin;
    void Refit(){draft.Tilt=draft.Origin is {} o?PzhTiltCompensation.Fit(o,draft.Shots):null;draft.Linear=PzhLinearCompensation.Fit(draft.LinearShots);}
    void Changed(){dirty=true;Refit();Render();}
    void ChangeMode(){draft.Mode=mode.SelectedIndex==1?PzhCalibrationMode.Linear:PzhCalibrationMode.Tilt;dirty=true;LoadCurrent();Render();}
    void LoadCurrent()
    {
        if(draft.Mode==PzhCalibrationMode.Linear&&c.State.Current.Target is {} target){SetAim(target);return;}
        var r=c.Result;if(r?.Azimuth is not {} a||r.High==null)return;SetAngles(a,(r.High.Min+r.High.Max)/2);
    }
    void LoadCurrentTarget(){if(c.State.Current.Target is {} target)SetAim(target);else MessageBox.Show(this,"请先在游戏或地图中选择瞄准点 A。","PZH校准");}
    void SetAngles(double a,double m){syncing=true;azimuth.Text=a.ToString("0.0",CultureInfo.InvariantCulture);mil.Text=m.ToString("0.0",CultureInfo.InvariantCulture);syncing=false;SyncAimFromAngles();}
    void SetAim(Coord aim){syncing=true;aimCoordinate.Text=aim.ToString();syncing=false;SyncAnglesFromAim();}
    void SyncAimFromAngles()
    {
        if(syncing||draft.Mode!=PzhCalibrationMode.Linear||c.State.Current.Origin is not {} origin||!TryParametersQuiet(out var a,out var m))return;
        var aim=c.Calculator.TargetFromHighArc(origin,a,m);if(aim==null)return;syncing=true;aimCoordinate.Text=aim.ToString();syncing=false;
    }
    void SyncAnglesFromAim()
    {
        if(syncing||draft.Mode!=PzhCalibrationMode.Linear||c.State.Current.Origin is not {} origin||Coordinates.Parse(aimCoordinate.Text,true) is not {} aim)return;
        var solution=c.Calculator.Solve(origin,aim,"spg");if(solution.Azimuth is not {} a||solution.High==null)return;syncing=true;azimuth.Text=a.ToString("0.0",CultureInfo.InvariantCulture);mil.Text=((solution.High.Min+solution.High.Max)/2).ToString("0.0",CultureInfo.InvariantCulture);syncing=false;
    }
    bool TryParametersQuiet(out double a,out double m)
    {
        a=m=0;if(!double.TryParse(azimuth.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!double.TryParse(mil.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out m)||!double.IsFinite(a)||!double.IsFinite(m)||m<=0)return false;a=(a%360+360)%360;return true;
    }
    bool TryParameters(out double a,out double m){if(TryParametersQuiet(out a,out m))return true;MessageBox.Show(this,"请输入有效的方位角和高抛密位。","PZH校准",MessageBoxButton.OK,MessageBoxImage.Information);return false;}
    bool TryAim(out Coord aim)
    {
        aim=Coordinates.Parse(aimCoordinate.Text,true)!;if(aim!=null)return true;
        if(c.State.Current.Origin is {} origin&&TryParametersQuiet(out var a,out var m)&&c.Calculator.TargetFromHighArc(origin,a,m) is {} calculated){aim=calculated;return true;}
        MessageBox.Show(this,"请输入有效的瞄准点 A 坐标，或有效的方位角和高抛密位。","PZH校准");return false;
    }
    void EnsureDraftOrigin(){if(draft.Origin!=null)return;draft.Map=c.State.Map;draft.Origin=c.State.Current.Origin;}
    void Add(Coord impact)
    {
        if(c.State.Current.Origin==null){MessageBox.Show(this,"请先设置炮位。","PZH校准");return;}
        EnsureDraftOrigin();if(!SameOrigin()){MessageBox.Show(this,"当前炮位已经变化。请先点“重置全部”，再为新位置添加落点。旧数据仍保留。","PZH校准");return;}
        if(draft.Mode==PzhCalibrationMode.Tilt){if(!TryParameters(out var a,out var m))return;draft.Shots.Add(new(a,m,impact));}
        else{if(!TryAim(out var aim))return;draft.LinearShots.Add(new(aim,impact));}
        manual.Clear();Changed();
    }
    void AddClipboard()=>c.CapturePzhCoordinate(coordinate=>Dispatcher.BeginInvoke(()=>Add(coordinate)));
    void AddManual(){var coordinate=Coordinates.Parse(manual.Text,true);if(coordinate==null){MessageBox.Show(this,"未识别到坐标，格式示例：x101.48 y120.16","PZH校准");manual.SelectAll();return;}Add(coordinate);}
    void RemoveSelected(){if(samples.SelectedIndex<0)return;if(draft.Mode==PzhCalibrationMode.Tilt)draft.Shots.RemoveAt(samples.SelectedIndex);else draft.LinearShots.RemoveAt(samples.SelectedIndex);Changed();}
    void Reset()
    {
        if(MessageBox.Show(this,"清空当前草稿中的全部倾斜和XY校准点，并改用现在的炮位重新校准？","PZH校准",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        draft.Shots.Clear();draft.Tilt=null;draft.LinearShots.Clear();draft.Linear=null;draft.Enabled=false;enabled.IsChecked=false;draft.NeedsReset=false;draft.Map=c.State.Map;draft.Origin=c.State.Current.Origin;Changed();
    }
    void Save()
    {
        Refit();var calibrated=draft.Mode==PzhCalibrationMode.Tilt?draft.Tilt!=null:draft.Linear!=null;
        if(draft.Enabled&&!calibrated){MessageBox.Show(this,draft.Mode==PzhCalibrationMode.Tilt?"倾斜模式至少需要两发有效落点才能开启补偿。":"XY模式至少需要一组瞄准点A和落点B才能开启补偿。","PZH校准");return;}
        draft.Updated=DateTime.Now;c.Pref.Pzh=draft.Copy();c.Save();c.Refresh();dirty=false;Render();
    }
    void Render()
    {
        var linear=draft.Mode==PzhCalibrationMode.Linear;linearAim.Visibility=linear?Visibility.Visible:Visibility.Collapsed;
        introDescription.Text=linear?"用于修正固定的地图坐标偏移。记录瞄准点 A 和实际落点 B 后，应用计算 X/Y 偏差，并把目标换算成补偿后的方位和高抛 MIL。":"SPH-2停在斜坡时，车体倾斜会让实际弹道偏离原始射表。记录至少两发不同方向的实际落点后，应用会估算当前停车姿态，并自动给出补偿后的高抛方位和MIL。";
        introUsage.Text=linear?"使用：输入或带入瞄准点 A → 开火并添加落点 B → 开启补偿并保存。":"使用：带入本发参数 → 开火并添加落点 → 至少两发后开启补偿并保存。";
        parameterTitle.Text=linear?"瞄准点 A 对应的射击参数":"本发实际射击参数";impactTitle.Text=linear?"实际落点 B 坐标":"落点坐标";
        samples.Items.Clear();
        if(linear)for(var i=0;i<draft.LinearShots.Count;i++){var s=draft.LinearShots[i];samples.Items.Add($"{i+1}.  A {s.Aim}  →  B {s.Impact}   Δx {s.Aim.X-s.Impact.X:+0.00;-0.00;0.00}  Δy {s.Aim.Y-s.Impact.Y:+0.00;-0.00;0.00}");}
        else for(var i=0;i<draft.Shots.Count;i++){var s=draft.Shots[i];samples.Items.Add($"{i+1}.  {s.Azimuth:0.0}°  {s.Mil:0} MIL   落点 {s.Impact}");}
        var match=SameOrigin();var calibrated=linear?draft.Linear!=null:draft.Tilt!=null;var count=linear?draft.LinearShots.Count:draft.Shots.Count;
        state.Text=c.PzhCoordinateCaptureWaiting?"等待下一次有效坐标…":draft.Origin==null?"尚未校准 · 请先设置炮位":!match?$"需要重置校准 · 记录炮位 {draft.Origin}，当前炮位 {c.State.Current.Origin?.ToString()??"未设置"}":!calibrated?$"当前炮位 · 已记录 {count} 组{(linear?"，至少需要1组":"，至少需要2发")}":linear?$"{(draft.Enabled?"补偿开启":"补偿关闭")} · {count}组 · Δx {draft.Linear!.X:+0.000;-0.000;0.000} · Δy {draft.Linear.Y:+0.000;-0.000;0.000} · 离散 {draft.Linear.RmsMeters:0.0} m":$"{(draft.Enabled?"补偿开启":"补偿关闭")} · {count}发 · 等效倾斜 {draft.Tilt!.MagnitudeDegrees:0.00}° · 拟合残差 {draft.Tilt.RmsDegrees:0.00}°";
    }
    void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(allowClose)return;if(dirty){var answer=MessageBox.Show(this,"校准数据已经修改，是否保存后关闭？","PZH校准",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(answer==MessageBoxResult.Cancel){e.Cancel=true;return;}if(answer==MessageBoxResult.Yes){Save();if(dirty){e.Cancel=true;return;}}}
        allowClose=true;c.CancelPzhCoordinateCapture(false);c.Updated-=ControllerUpdated;c.CalibrationWindowClosed(this);
    }
}
