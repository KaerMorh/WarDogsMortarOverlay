using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WarDogs.Multiplayer;

// Independent native tool window. Visual refinement is deferred.
public sealed class TaskPickerWindow : Window
{
    sealed record Choice(string RoomId, string Uid, string SessionId, RoomTask Task, string Name)
    {
        public string Key => RoomId + "/" + Uid + "/" + SessionId + "/" + Task.Id;
    }
    readonly Controller c;
    readonly ListBox list = new() { Background = Brushes.Transparent, Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock connection = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBlock members = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    readonly Button publish;
    List<Choice> choices = new();
    string stamp = "";
    bool initialized;
    bool dirty;
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(200) };
    internal string? HighlightedTaskId => list.SelectedItem is ListBoxItem {Tag:Choice choice} ? choice.Task.Id : null;

    public TaskPickerWindow(Controller controller)
    {
        c = controller; Title = "WarDogs · 联机任务"; Width = 420; Height = 570; MinWidth = 340; MinHeight = 330;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResize; Topmost = true; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(20, 25, 35)); Foreground = Brushes.WhiteSmoke;
        var root = new DockPanel { Margin = new Thickness(10), Background = Background }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(connection);
        var summary = new Expander { Header = "成员 / 炮位", IsExpanded = c.Pref.Multiplayer.Role != "scout", Content = new ScrollViewer { Content = members, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; top.Children.Add(summary);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; top.Children.Add(actions);
        Button Button(Panel parent, string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(8, 6, 8, 6) }; button.Click += (_, _) => action(); parent.Children.Add(button); return button;
        }
        publish = Button(actions, "发布任务", () => c.Act("publishTask"));
        Button(actions, "联机设置", () => c.Hud.OpenSettings("room"));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var selection = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; bottom.Children.Add(selection);
        Button(selection, "上一条", () => MoveSelection(-1)); Button(selection, "下一条", () => MoveSelection(1)); Button(selection, "解算", Confirm); Button(selection, "关闭", Close);
        bottom.Children.Add(new TextBlock { Text = "拖动标题栏移动窗口 · 双击任务解算\n关闭窗口保持联机，退出房间请进入联机设置。", FontSize = 10, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(list);

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(49, 87, 129)))); itemStyle.Triggers.Add(selected);
        list.ItemContainerStyle = itemStyle;
        refresh.Tick += (_, _) => { if(dirty){dirty=false;Render();} };
        list.MouseDoubleClick += (_, _) => Confirm();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Enter) { Confirm(); e.Handled = true; } };
        Loaded += (_, _) =>
        {
            if (!c.Demo) c.SetTaskMenuHotkeys(true); c.Updated += RequestRender; Render(); refresh.Start();
            Left = Math.Clamp(c.Hud.Left + c.Hud.ActualWidth + 10, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
            Top = Math.Clamp(c.Hud.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
        };
        Closed += (_, _) => { c.Updated -= RequestRender; refresh.Stop(); if (!c.Demo) c.SetTaskMenuHotkeys(false); };
        ShowActivated = false;
    }

    void RequestRender() => dirty = true;

    void Render()
    {
        var state = c.Rooms.State;
        connection.Text = (c.Demo && state.RoomId.Length > 0 ? "演示房间 · " + state.RoomCode : c.Rooms.Status) + " · " + (c.Pref.Multiplayer.Role == "scout" ? "侦察兵" : "炮兵") + (state.Map.Length > 0 && state.Map != c.State.Map ? " · 地图不同" : "");
        publish.Content = c.Rooms.Capture.Waiting ? "等待复制 · 再按取消" : "发布任务"; publish.IsEnabled = c.Rooms.Connected;
        publish.ToolTip = c.Pref.Keys.GetValueOrDefault("publishTask", "");
        var nextStamp = state.RoomId + ":" + state.Revision + ":" + c.State.Map + ":" + c.State.Current.Target;
        if (nextStamp == stamp) return; stamp = nextStamp;
        members.Text = string.Join("\n", state.Members.Values.OrderBy(m => m.Joined).Select(m =>
            $"{m.DisplayName} · {(m.Role == "scout" ? "侦察兵" : "炮兵")}{(m.Online ? "" : " · 离线（保留三分钟）")}" +
            (m.Role == "gunner" ? "\n炮位 " + (m.Origin?.Coordinate.ToString() ?? "—") + (m.Target != null ? " · 正在解算 " + m.Target.Coordinate : "") : "")));
        if (state.Members.Count == 0) members.Text = "暂无成员 · 请先在设置中加入房间";
        var selectedKey = list.SelectedItem is ListBoxItem { Tag: Choice selectedChoice } ? selectedChoice.Key : null;
        choices = state.Members.Values.OrderBy(m => m.Joined).SelectMany(m => m.Tasks.OrderByDescending(t => t.Sequence).Select(t => new Choice(state.RoomId, m.Uid, m.SessionId, t, m.DisplayName))).ToList();
        var scroll = FindScroll(list); var offset = scroll?.VerticalOffset ?? 0;
        list.Items.Clear();
        foreach (var choice in choices)
        {
            var task = choice.Task; var current = task.Point.Map == c.State.Map && task.Point.Coordinate == c.State.Current.Target;
            var status = task.SolvedBy.Count == 0 ? "尚未解算" : "已解算：" + string.Join("、", task.SolvedBy.Select(x => x.Name));
            var solving = state.Members.Values.Where(m => m.Online && m.Role == "gunner" && m.Solved && m.Target?.Same(task.Point) == true).Select(m => m.DisplayName).ToArray();
            if (solving.Length > 0) status += " · 正在解算：" + string.Join("、", solving);
            list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = $"{(current ? "● 当前目标 · " : "")}{choice.Name} · 任务 {task.Sequence}\n{task.Point.Coordinate} · {task.Point.Map}\n{status}", TextWrapping = TextWrapping.Wrap, FontSize = 12 }, Padding = new Thickness(7), Tag = choice });
        }
        list.SelectedIndex = selectedKey == null ? (!initialized && choices.Count > 0 ? 0 : -1) : choices.FindIndex(x => x.Key == selectedKey);
        list.UpdateLayout(); scroll?.ScrollToVerticalOffset(offset);
        initialized = true;
    }
    static ScrollViewer? FindScroll(DependencyObject root)
    {
        if(root is ScrollViewer scroll)return scroll;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var result=FindScroll(VisualTreeHelper.GetChild(root,i));if(result!=null)return result;}
        return null;
    }
    public void MoveSelection(int direction)
    {
        if (choices.Count == 0) return;
        list.SelectedIndex = list.SelectedIndex < 0 ? (direction < 0 ? choices.Count - 1 : 0) : (list.SelectedIndex + direction + choices.Count) % choices.Count;
        list.ScrollIntoView(list.SelectedItem);
    }
    public void Confirm()
    {
        if (list.SelectedItem is not ListBoxItem { Tag: Choice choice }) return;
        var state = c.Rooms.State;
        if (!c.Rooms.Connected || state.RoomId != choice.RoomId || !state.Members.TryGetValue(choice.Uid, out var member) || member.SessionId != choice.SessionId || !member.Tasks.Any(t => t.Id == choice.Task.Id))
        { c.Notify("任务已失效 · 请重新选择或从历史解算"); return; }
        c.Rooms.Select(choice.Task.Point, "房间任务 · " + choice.Name);
    }
}
