using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarDogs;
public sealed class UpdatePanel : StackPanel
{
    public static StackPanel Badge(string caption, bool visible)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.OrangeRed,
            Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = visible ? Visibility.Visible : Visibility.Collapsed });
        return row;
    }
    public UpdatePanel(Controller c)
    {
        var service = c.Updates;
        Margin = new Thickness(0);
        var title = new TextBlock { FontSize = 16, Foreground = Brushes.White };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = Brushes.LightSteelBlue };
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
        var progress = new ProgressBar { Height = 5, Maximum = 100, Margin = new Thickness(0, 0, 0, 8) };
        var auto = new CheckBox { Content = "启动时自动检查更新", IsChecked = c.Pref.AutoCheckUpdates, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 10) };
        auto.Click += (_, _) => { c.Pref.AutoCheckUpdates = auto.IsChecked == true; c.Refresh(); c.Save(); };
        var actions = new WrapPanel();
        Button Button(string text) { var b = new Button { Content = text, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 4, 4), FontSize = 11 }; actions.Children.Add(b); return b; }
        var check = Button("检查更新"); check.Click += async (_, _) => await service.CheckAsync();
        var download = Button("下载更新"); download.Click += async (_, _) => await service.DownloadAsync();
        var install = Button("重启并安装"); install.Click += async (_, _) => await service.InstallAsync();
        var cancel = Button("取消下载"); cancel.Click += (_, _) => service.Cancel();
        var website = Button("发布页"); website.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(UpdateManifest.Repository + "/releases/latest") { UseShellExecute = true }); } catch { c.Notify("无法打开浏览器"); } };
        Children.Add(title); Children.Add(status); Children.Add(notes); Children.Add(progress); Children.Add(auto); Children.Add(actions);
        Children.Add(new TextBlock { Text="下载完成后退出可保留安装包，下次打开继续安装；升级后自动清理旧包。", TextWrapping=TextWrapping.Wrap, Foreground=Brushes.LightSteelBlue, FontSize=11, Margin=new Thickness(0,8,0,0) });
        void Render()
        {
            title.Text = "软件更新 · 当前 " + UpdateService.CurrentVersion;
            auto.IsChecked = c.Pref.AutoCheckUpdates;
            status.Text = service.Status + (service.IsDownloading && service.Progress > 0 ? $" ({service.Progress:0}%)" : "");
            notes.Text = service.Available is {} update ? "新版 " + update.Version + "\n" + update.Notes : "";
            check.IsEnabled = !service.Busy;
            download.Visibility = service.HasUpdate && !service.Ready ? Visibility.Visible : Visibility.Collapsed; download.IsEnabled = !service.Busy;
            install.Visibility = service.Ready ? Visibility.Visible : Visibility.Collapsed; install.IsEnabled = !service.Busy;
            cancel.Visibility = service.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
            progress.Value = service.Progress; progress.Visibility = service.IsDownloading || service.Ready ? Visibility.Visible : Visibility.Collapsed;
        }
        Loaded += (_, _) => { c.Updated += Render; Render(); };
        Unloaded += (_, _) => c.Updated -= Render;
        Render();
    }
}
