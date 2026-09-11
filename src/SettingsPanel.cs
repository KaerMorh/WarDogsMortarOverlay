using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace WarDogs;
public class SettingsPanel:StackPanel
{
    public SettingsPanel(Controller c,bool compact=false)
    {
        TextBlock Label(string t,double size=12)=>new(){Text=t,FontSize=size,Foreground=(Brush)new BrushConverter().ConvertFromString("#ACB8CA")!,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,6)};
        Children.Add(new Border { Child=new UpdatePanel(c), Background=(Brush)new BrushConverter().ConvertFromString("#202E42")!, BorderBrush=(Brush)new BrushConverter().ConvertFromString("#536781")!, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(12), Margin=new Thickness(0,0,0,12) });
        Children.Add(new Border { Height=2, Background=(Brush)new BrushConverter().ConvertFromString("#536781")!, Margin=new Thickness(0,0,0,12) });
        Children.Add(Label("测试功能",16));
        var testing=new CheckBox{Content="开启测试功能（显示联机设置）",IsChecked=c.Pref.EnableTestFeatures,Foreground=Brushes.White,Margin=new Thickness(0,4,0,12)};
        testing.Click+=(s,e)=>{c.Pref.EnableTestFeatures=testing.IsChecked==true;c.Save();c.Hud?.RefreshTestFeatures();};Children.Add(testing);
        Children.Add(new Border{Height=1,Background=(Brush)new BrushConverter().ConvertFromString("#303B4D")!,Margin=new Thickness(0,4,0,12)});
        Children.Add(Label("快捷键",16));Children.Add(Label("直接输入组合键名称，或点“录入”后按键；每项单独保存。",11));
        foreach(var item in Controller.Actions)
        {
            Children.Add(Label(item.Value));var row=new DockPanel{Margin=new Thickness(0,0,0,5)};
            Button Small(string t)=>new(){Content=t,Padding=new Thickness(7,5,7,5),Margin=new Thickness(4,0,0,0),FontSize=11};
            var save=Small("保存");DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
            var record=Small("录入");DockPanel.SetDock(record,Dock.Right);row.Children.Add(record);
            var clear=Small("清除");DockPanel.SetDock(clear,Dock.Right);row.Children.Add(clear);
            var box=new TextBox{Text=c.Pref.Keys.GetValueOrDefault(item.Key,""),FontSize=11,Padding=new Thickness(7,5,7,5),MinWidth=90};row.Children.Add(box);bool recording=false;
            void RegisteredKey(string gesture){if(recording){box.Text=gesture;recording=false;c.IsRecordingHotkey=false;}}
            Loaded+=(s,e)=>{box.Text=c.Pref.Keys.GetValueOrDefault(item.Key,"");c.HotkeyRecorded+=RegisteredKey;};Unloaded+=(s,e)=>{c.HotkeyRecorded-=RegisteredKey;recording=false;};
            record.Click+=(s,e)=>{recording=true;c.IsRecordingHotkey=true;box.Text="请按快捷键…";box.Focus();};
            box.PreviewKeyDown+=(s,e)=>{if(!recording)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
                if(key==Key.Escape)box.Text=c.Pref.Keys.GetValueOrDefault(item.Key,"");else{try{box.Text=new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key,Keyboard.Modifiers))??"";}catch{box.Text="";c.Notify("请使用修饰键组合，或 F1–F24");}}recording=false;c.IsRecordingHotkey=false;};
            box.LostKeyboardFocus+=(s,e)=>{if(recording){recording=false;c.IsRecordingHotkey=false;box.Text=c.Pref.Keys.GetValueOrDefault(item.Key,"");}};
            clear.Click+=(s,e)=>{if(c.Bind(item.Key,""))box.Text="";};save.Click+=(s,e)=>{if(!c.Bind(item.Key,box.Text))box.Text=c.Pref.Keys.GetValueOrDefault(item.Key,"");};Children.Add(row);
        }
        Children.Add(new Border{Height=1,Background=(Brush)new BrushConverter().ConvertFromString("#303B4D")!,Margin=new Thickness(0,16,0,12)});
        Children.Add(Label("悬浮窗外观",16));Children.Add(Label("面板背景浓度（数字保持清晰）",11));
        var opacity=new Slider{Minimum=.25,Maximum=1,Value=c.Pref.HudOpacity,Margin=new Thickness(0,4,0,12)};opacity.ValueChanged+=(s,e)=>{c.Pref.HudOpacity=e.NewValue;c.Refresh();};Children.Add(opacity);
        void Alpha(string title,double value,Action<double> set){var label=Label($"{title} · {value:P0}",11);Children.Add(label);var slider=new Slider{Minimum=0,Maximum=1,Value=value,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,4,0,12)};slider.ValueChanged+=(s,e)=>{set(e.NewValue);label.Text=$"{title} · {e.NewValue:P0}";c.Refresh();};Children.Add(slider);}
        Alpha("按键背景不透明度",c.Pref.HudButtonOpacity,v=>c.Pref.HudButtonOpacity=v);
        Alpha("数值 / 信息背景不透明度",c.Pref.HudTileOpacity,v=>c.Pref.HudTileOpacity=v);
        var bubble=new CheckBox{Content="小球极简化：右侧显示纯文字读数",IsChecked=c.Pref.BubbleReadout,Foreground=Brushes.White,Margin=new Thickness(0,4,0,12)};
        bubble.Checked+=(s,e)=>{c.Pref.BubbleReadout=true;c.Refresh();};bubble.Unchecked+=(s,e)=>{c.Pref.BubbleReadout=false;c.Refresh();};Children.Add(bubble);
        Children.Add(Label("小球读数文字颜色（#RRGGBB）",11));
        var colorRow=new DockPanel{Margin=new Thickness(0,0,0,8)};var applyColor=new Button{Content="应用颜色",Padding=new Thickness(8,5,8,5)};DockPanel.SetDock(applyColor,Dock.Right);colorRow.Children.Add(applyColor);
        var colorInput=new TextBox{Text=c.Pref.BubbleTextColor,Padding=new Thickness(7,5,7,5),FontSize=12};colorRow.Children.Add(colorInput);Children.Add(colorRow);
        void SetColor(string value){if(!System.Text.RegularExpressions.Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")){c.Notify("颜色格式无效 · 请输入 #RRGGBB，例如 #91C5FF");return;}c.Pref.BubbleTextColor=value.ToUpperInvariant();colorInput.Text=c.Pref.BubbleTextColor;c.Refresh();c.Save();}
        applyColor.Click+=(s,e)=>SetColor(colorInput.Text.Trim());
        var palette=new WrapPanel();foreach(var (name,hex) in new[]{("蓝色","#91C5FF"),("白色","#F5F7FB"),("橙色","#FFBD87"),("绿色","#8EE6AE")}){var b=new Button{Content=name,Foreground=(Brush)new BrushConverter().ConvertFromString(hex)!,Padding=new Thickness(10,5,10,5)};b.Click+=(s,e)=>SetColor(hex);palette.Children.Add(b);}Children.Add(palette);
        Children.Add(Label("整体大小 75%–150%",11));var scale=new Slider{Minimum=.75,Maximum=1.5,Value=c.Pref.HudScale,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,4,0,12)};scale.ValueChanged+=(s,e)=>{c.Pref.HudScale=e.NewValue;c.Refresh();};Children.Add(scale);
        var forms=new WrapPanel();foreach(var (name,form) in new[]{("控制面板","panel"),("简化界面","compact"),("小圆球","bubble")}){var b=new Button{Content=name,Padding=new Thickness(8,6,8,6),FontSize=11};b.Click+=(s,e)=>c.Hud.SetForm(form);forms.Children.Add(b);}Children.Add(forms);
        if(!compact)Children.Add(Label("设置与 HUD 同步保存。关闭大型地图只隐藏工作台，不影响 HUD 或坐标接收。",12));
        Unloaded+=(s,e)=>c.IsRecordingHotkey=false;
    }
}
