using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarDogs.Multiplayer;

public sealed class RoomSettingsPanel : StackPanel
{
    public RoomSettingsPanel(Controller c)
    {
        var pref = c.Pref.Multiplayer;
        TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 3) };
        TextBox Input(string value) => new() { Text = value, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 5) };
        Children.Add(Label("联机房间"));
        Children.Add(Label("服务地址")); var address = Input(pref.ServerUrl); Children.Add(address);
        Children.Add(Label("房间码 · 4–32 位字母、数字、_ 或 -")); var room = Input(pref.Room); Children.Add(room);
        Children.Add(Label("Callsign")); var name = Input(pref.Callsign); Children.Add(name);
        var role = new ComboBox { ItemsSource = new[] { "炮兵", "侦察兵（精简模式）" }, Foreground = Brushes.Black, SelectedIndex = pref.Role == "scout" ? 1 : 0, Margin = new Thickness(0, 4, 0, 6) };
        var roleTextStyle = new Style(typeof(TextBlock)); roleTextStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Black)); role.Resources.Add(typeof(TextBlock), roleTextStyle); Children.Add(role);
        var roleText = new FrameworkElementFactory(typeof(TextBlock)); roleText.SetValue(TextBlock.ForegroundProperty, Brushes.Black); roleText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); role.ItemTemplate = new DataTemplate { VisualTree = roleText };
        var auto = new CheckBox { Content = "启动时自动进入上次房间", IsChecked = pref.AutoJoin, Margin = new Thickness(0, 5, 0, 5) }; Children.Add(auto);
        var share = new CheckBox { Content = "自动共享当前解算目标", IsChecked = pref.ShareTarget, Margin = new Thickness(0, 5, 0, 5) }; Children.Add(share);
        Children.Add(Label("关闭共享后停止更新，对方保留最后收到的目标。"));
        bool refreshing=false;
        void Changed(){c.Rooms.PreferencesChanged();c.Refresh();}
        // Save only the edited field: another settings surface may contain old
        // checkbox values and must never silently reenable target sharing.
        address.LostKeyboardFocus += (_, _) => {pref.ServerUrl=address.Text.Trim();Changed();};
        room.LostKeyboardFocus += (_, _) => {pref.Room=room.Text.Trim().ToLowerInvariant();Changed();};
        name.LostKeyboardFocus += (_, _) => {pref.Callsign=name.Text.Trim();Changed();};
        role.SelectionChanged += (_, _) => {if(!refreshing){pref.Role=role.SelectedIndex==1?"scout":"gunner";Changed();}};
        auto.Click += (_, _) => {pref.AutoJoin=auto.IsChecked==true;Changed();};
        share.Click += (_, _) => {pref.ShareTarget=share.IsChecked==true;Changed();};
        var row = new WrapPanel();
        var join = new Button { Content = "加入 / 创建房间", Padding = new Thickness(9, 6, 9, 6) };
        join.Click += async (_, _) => { join.IsEnabled = false; try { await c.Rooms.JoinAsync(); } finally { join.IsEnabled = true; } };
        var leave = new Button { Content = "退出房间", Padding = new Thickness(9, 6, 9, 6) }; leave.Click += async (_, _) => await c.Rooms.LeaveAsync();
        row.Children.Add(join); row.Children.Add(leave); Children.Add(row);
        var open = new Button { Content = "打开独立联机任务窗口", Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 6, 0, 6) }; open.Click += (_, _) => c.ShowRoomTasks(); Children.Add(open);
        var connection = Label(c.Rooms.Status);
        void Render()
        {
            refreshing=true;
            try
            {
                connection.Text=c.Rooms.Status;
                if(!address.IsKeyboardFocusWithin)address.Text=pref.ServerUrl;
                if(!room.IsKeyboardFocusWithin)room.Text=pref.Room;
                if(!name.IsKeyboardFocusWithin)name.Text=pref.Callsign;
                role.SelectedIndex=pref.Role=="scout"?1:0;auto.IsChecked=pref.AutoJoin;share.IsChecked=pref.ShareTarget;
            }
            finally{refreshing=false;}
        }
        Loaded += (_, _) => { c.Updated += Render; Render(); }; Unloaded += (_, _) => c.Updated -= Render;
        Children.Add(connection);
    }
}
