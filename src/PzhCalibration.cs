using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarDogs;

public class PzhCalibrationSettings
{
    public bool Enabled{get;set;}
    public bool NeedsReset{get;set;}
    public string Map{get;set;}="";
    public Coord? Origin{get;set;}
    public List<PzhShot> Shots{get;set;}=[];
    public PzhTilt? Tilt{get;set;}
    public DateTime? Updated{get;set;}
    public PzhCalibrationSettings Copy()=>new(){Enabled=Enabled,NeedsReset=NeedsReset,Map=Map,Origin=Origin,Shots=Shots.ToList(),Tilt=Tilt,Updated=Updated};
}

public record PzhDisplaySolution(Solution Raw,PzhCorrected? Corrected,bool Active,string CalibrationStatus);

public class PzhCalibrationWindow:Window
{
    readonly Controller c;PzhCalibrationSettings draft;readonly ListBox samples=new();
    readonly TextBox azimuth=new(){Width=92},mil=new(){Width=92},manual=new(){MinWidth=210};
    readonly CheckBox enabled=new(){Content="开启倾斜补偿",Foreground=Brushes.White};
    readonly TextBlock state=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue};
    bool dirty,allowClose;
    public PzhCalibrationWindow(Controller controller)
    {
        c=controller;draft=c.Pref.Pzh.Copy();Title="PZH 倾斜校准";Width=520;Height=590;MinWidth=480;MinHeight=500;
        Background=(Brush)new BrushConverter().ConvertFromString("#10141C")!;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        Button B(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(12,7,12,7)};if(primary)b.SetResourceReference(StyleProperty,"Primary");b.Click+=(s,e)=>action();return b;}
        footer.Children.Add(B("保存",Save,true));footer.Children.Add(B("关闭",Close));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var intro=new StackPanel();
        intro.Children.Add(new TextBlock{Text="这是做什么的？",FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        intro.Children.Add(new TextBlock{Text="SPH-2停在斜坡时，车体倾斜会让实际弹道偏离原始射表。记录至少两发不同方向的实际落点后，应用会估算当前停车姿态，并自动给出补偿后的高抛方位和MIL。",TextWrapping=TextWrapping.Wrap,LineHeight=20});
        intro.Children.Add(new TextBlock{Text="使用：带入本发参数 → 开火并添加落点 → 至少两发后开启补偿并保存。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,7,0,0)});
        panel.Children.Add(new Border{Child=intro,Background=(Brush)new BrushConverter().ConvertFromString("#1A2432")!,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#536781")!,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)});
        var warning=new TextBlock{Text="车辆换位置后需要重新校准。炮位变化只会停用旧补偿，不会删除已有数据。",Foreground=(Brush)new BrushConverter().ConvertFromString("#FFBD87")!,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};panel.Children.Add(warning);
        enabled.IsChecked=draft.Enabled;enabled.Click+=(s,e)=>{draft.Enabled=enabled.IsChecked==true;Changed();};panel.Children.Add(enabled);
        state.Margin=new Thickness(0,8,0,12);panel.Children.Add(state);
        panel.Children.Add(new TextBlock{Text="本发实际射击参数",FontSize=15,Margin=new Thickness(0,4,0,7)});
        var fields=new StackPanel{Orientation=Orientation.Horizontal};fields.Children.Add(new TextBlock{Text="方位°",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0)});fields.Children.Add(azimuth);fields.Children.Add(new TextBlock{Text="高抛 MIL",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,6,0)});fields.Children.Add(mil);fields.Children.Add(B("带入当前原始解",LoadCurrent));panel.Children.Add(fields);
        panel.Children.Add(new TextBlock{Text="落点坐标",FontSize=15,Margin=new Thickness(0,14,0,7)});
        var input=new DockPanel();var addManual=B("手动添加",AddManual,true);DockPanel.SetDock(addManual,Dock.Right);input.Children.Add(addManual);var addClipboard=B("从剪贴板添加",AddClipboard);DockPanel.SetDock(addClipboard,Dock.Right);input.Children.Add(addClipboard);manual.ToolTip="x101.48 y120.16";input.Children.Add(manual);panel.Children.Add(input);
        panel.Children.Add(new TextBlock{Text="先填本发实际方位和密位，再添加这一发落点。可反复增删后重新保存。",Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)});
        samples.Height=215;panel.Children.Add(samples);
        var edits=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};edits.Children.Add(B("删除选中",RemoveSelected));edits.Children.Add(B("重置全部",Reset));panel.Children.Add(edits);
        Closing+=OnClosing;c.Updated+=ControllerUpdated;LoadCurrent();Render();
    }
    void ControllerUpdated()=>Dispatcher.BeginInvoke(()=>
    {
        if(draft.Origin!=null&&!draft.NeedsReset&&(draft.Map!=c.State.Map||draft.Origin!=c.State.Current.Origin)){draft.NeedsReset=true;dirty=true;}
        Render();
    });
    bool SameOrigin()=>!draft.NeedsReset&&draft.Map==c.State.Map&&draft.Origin==c.State.Current.Origin;
    void Changed(){dirty=true;draft.Tilt=draft.Origin is {} o?PzhTiltCompensation.Fit(o,draft.Shots):null;Render();}
    void LoadCurrent()
    {
        var r=c.Result;if(r?.Azimuth is not {} a||r.High==null)return;
        azimuth.Text=a.ToString("0.0");mil.Text=((r.High.Min+r.High.Max)/2).ToString("0");
    }
    bool TryParameters(out double a,out double m)
    {
        a=m=0;if(!double.TryParse(azimuth.Text,out a)||!double.TryParse(mil.Text,out m)||!double.IsFinite(a)||!double.IsFinite(m)||m<=0){MessageBox.Show(this,"请输入有效的方位角和高抛密位。","PZH校准",MessageBoxButton.OK,MessageBoxImage.Information);return false;}a=(a%360+360)%360;return true;
    }
    void EnsureDraftOrigin()
    {
        if(draft.Origin!=null)return;draft.Map=c.State.Map;draft.Origin=c.State.Current.Origin;
    }
    void Add(Coord impact)
    {
        if(c.State.Current.Origin==null){MessageBox.Show(this,"请先设置炮位。","PZH校准");return;}
        EnsureDraftOrigin();
        if(!SameOrigin()){MessageBox.Show(this,"当前炮位已经变化。请先点“重置全部”，再为新位置添加落点。旧数据仍保留。","PZH校准");return;}
        if(!TryParameters(out var a,out var m))return;draft.Shots.Add(new(a,m,impact));manual.Clear();Changed();
    }
    void AddClipboard()=>c.CapturePzhCoordinate(coordinate=>Dispatcher.BeginInvoke(()=>Add(coordinate)));
    void AddManual(){var coordinate=Coordinates.Parse(manual.Text,true);if(coordinate==null){MessageBox.Show(this,"未识别到坐标，格式示例：x101.48 y120.16","PZH校准");manual.SelectAll();return;}Add(coordinate);}
    void RemoveSelected(){if(samples.SelectedIndex<0)return;draft.Shots.RemoveAt(samples.SelectedIndex);Changed();}
    void Reset()
    {
        if(MessageBox.Show(this,"清空当前草稿中的全部落点，并改用现在的炮位重新校准？","PZH校准",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        draft.Shots.Clear();draft.Tilt=null;draft.Enabled=false;enabled.IsChecked=false;draft.NeedsReset=false;draft.Map=c.State.Map;draft.Origin=c.State.Current.Origin;Changed();
    }
    void Save()
    {
        draft.Tilt=draft.Origin is {} o?PzhTiltCompensation.Fit(o,draft.Shots):null;
        if(draft.Enabled&&draft.Tilt==null){MessageBox.Show(this,"至少需要两发有效落点才能开启补偿。","PZH校准");return;}
        draft.Updated=DateTime.Now;c.Pref.Pzh=draft.Copy();c.Save();c.Refresh();dirty=false;Render();
    }
    void Render()
    {
        samples.Items.Clear();for(var i=0;i<draft.Shots.Count;i++){var s=draft.Shots[i];samples.Items.Add($"{i+1}.  {s.Azimuth:0.0}°  {s.Mil:0} MIL   落点 {s.Impact}");}
        var match=SameOrigin();var fit=draft.Tilt;
        state.Text=c.PzhCoordinateCaptureWaiting?"等待下一次有效落点坐标…":draft.Origin==null?"尚未校准 · 请先设置炮位":!match?$"需要重置校准 · 记录炮位 {draft.Origin}，当前炮位 {c.State.Current.Origin?.ToString()??"未设置"}":fit==null?$"当前炮位 · 已记录 {draft.Shots.Count} 发，至少需要2发":$"{(draft.Enabled?"补偿开启":"补偿关闭")} · {draft.Shots.Count}发 · 等效倾斜 {fit.MagnitudeDegrees:0.00}° · 拟合残差 {fit.RmsDegrees:0.00}°";
    }
    void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(allowClose)return;
        if(dirty)
        {
            var answer=MessageBox.Show(this,"校准数据已经修改，是否保存后关闭？","PZH校准",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(answer==MessageBoxResult.Cancel){e.Cancel=true;return;}
            if(answer==MessageBoxResult.Yes){Save();if(dirty){e.Cancel=true;return;}}
        }
        allowClose=true;c.CancelPzhCoordinateCapture(false);c.Updated-=ControllerUpdated;c.CalibrationWindowClosed(this);
    }
}
