using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarDogs;

public sealed class DeepSeekOcrPanel:StackPanel
{
    public DeepSeekOcrPanel(Controller c)
    {
        var settings=c.Pref.DeepSeekOcr;var muted=(Brush)new BrushConverter().ConvertFromString("#ACB8CA")!;
        TextBlock Text(string value)=>new(){Text=value,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,5)};Button Button(string value)=>new(){Content=value,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,2,6,4)};
        var enabled=new CheckBox{Content="DeepSeek OCR",Foreground=Brushes.White,IsChecked=settings.Enabled,Margin=new Thickness(12,3,0,8)};Children.Add(enabled);var details=new StackPanel{Margin=new Thickness(24,0,0,0),Visibility=settings.Enabled?Visibility.Visible:Visibility.Collapsed};Children.Add(details);
        details.Children.Add(Text("在游戏内按快捷键截取地图并识别坐标。截图会发送到 DeepSeek，API 按用量收费。"));
        var keyState=Text(c.Ocr.HasKey?"API Key：已保存":"API Key：未配置");var key=new PasswordBox{Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,2,0,5)};details.Children.Add(keyState);details.Children.Add(key);
        var keyButtons=new WrapPanel();var save=Button("保存 API Key");var clear=Button("清除");var help=Button("如何获取 API Key");keyButtons.Children.Add(save);keyButtons.Children.Add(clear);keyButtons.Children.Add(help);details.Children.Add(keyButtons);
        var test=Button("测试 API（文字＋图片）");details.Children.Add(test);details.Children.Add(Text("测试会发送内置测试图及文字，产生少量 API 费用；不会上传当前屏幕。"));
        var hotkeys=new Expander{Header="快捷键设置",Foreground=Brushes.White,Margin=new Thickness(0,6,0,6)};var hotkeyBody=new StackPanel();hotkeyBody.Children.Add(Text("设炮位（默认 Ctrl+1）"));hotkeyBody.Children.Add(new HotkeyEditor(c,"ocrOrigin"));hotkeyBody.Children.Add(Text("选目标（默认 Ctrl+2）"));hotkeyBody.Children.Add(new HotkeyEditor(c,"ocrTarget"));hotkeys.Content=hotkeyBody;details.Children.Add(hotkeys);
        var region=Text(RegionText());details.Children.Add(region);var regionButtons=new WrapPanel();var calibrate=Button("校准地图区域");var reset=Button("恢复默认区域");regionButtons.Children.Add(calibrate);regionButtons.Children.Add(reset);details.Children.Add(regionButtons);
        var status=Text("");status.Foreground=Brushes.White;details.Children.Add(status);
        enabled.Click+=(_,_)=>{settings.Enabled=enabled.IsChecked==true;details.Visibility=settings.Enabled?Visibility.Visible:Visibility.Collapsed;c.SetOcrEnabled(settings.Enabled);};
        save.Click+=(_,_)=>{if(c.Ocr.SaveKey(key.Password)){key.Clear();keyState.Text="API Key：已保存";}};clear.Click+=(_,_)=>{key.Clear();c.Ocr.ClearKey();keyState.Text="API Key：未配置";};
        help.Click+=(_,_)=>{var answer=MessageBox.Show("1. 登录 DeepSeek 开放平台。\n2. 在 API Keys 页面创建并复制 Key。\n3. 确保账户有可用余额，再回到这里保存并测试。\n\nAPI 调用按用量收费；地图截图会发送到 DeepSeek。\n\n是否打开 API Key 页面？","如何获取 DeepSeek API Key",MessageBoxButton.YesNo,MessageBoxImage.Information);if(answer==MessageBoxResult.Yes)try{Process.Start(new ProcessStartInfo("https://platform.deepseek.com/api_keys"){UseShellExecute=true});}catch{try{Clipboard.SetText("https://platform.deepseek.com/api_keys");c.Notify("API Key 页面无法打开 · 链接已复制");}catch{c.Notify("API Key 页面无法打开 · https://platform.deepseek.com/api_keys");}}};
        test.Click+=async(_,_)=>await c.Ocr.TestAsync();calibrate.Click+=(_,_)=>c.Ocr.ArmCalibration();reset.Click+=(_,_)=>{c.Ocr.ResetRegion();region.Text=RegionText();};
        string RegionText()=>settings.CustomRegion?$"地图区域：{settings.RegionX:P1}, {settings.RegionY:P1}, {settings.RegionWidth:P1} × {settings.RegionHeight:P1}":"地图区域：默认中央区域";
        void Refresh(){enabled.IsChecked=settings.Enabled;details.Visibility=settings.Enabled?Visibility.Visible:Visibility.Collapsed;test.IsEnabled=!c.Ocr.Busy;status.Text=$"状态：{c.Ocr.StatusText??"就绪"}\n测试：{c.Ocr.TestStatus}";region.Text=RegionText();}
        Loaded+=(_,_)=>{c.Updated+=Refresh;Refresh();};Unloaded+=(_,_)=>{c.Updated-=Refresh;c.Ocr.CancelCalibration();};
    }
}
