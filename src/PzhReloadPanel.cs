using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WarDogs;

public sealed class PzhReloadPanel : StackPanel
{
    public PzhReloadPanel(Controller c)
    {
        var settings = c.Pref.PzhReload;
        var muted = (Brush)new BrushConverter().ConvertFromString("#ACB8CA")!;
        TextBlock Text(string value) => new() { Text = value, Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 5) };
        Button Button(string value) => new() { Content = value, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 6, 4) };
        Children.Add(Text("PZH 自动装弹（实验）"));
        Children.Add(Text("自动输入仅在 SPH-2 且测试功能总开关开启时运行。射击后在游戏中按换弹键；Esc 取消。"));
        var enabled = new CheckBox { Content = "开启 PZH 自动装弹", Foreground = Brushes.White, IsChecked = settings.Enabled, Margin = new Thickness(0, 3, 0, 8) };
        enabled.Click += (_, _) => { settings.Enabled = enabled.IsChecked == true; c.Reload.RefreshHook(); c.Save(); };
        Children.Add(enabled);
        Children.Add(Text("换弹键（单个字母或数字，默认 R）"));
        var keyRow = new WrapPanel();
        var key = new TextBox { Text = settings.TriggerKey, Width = 65, Padding = new Thickness(6), Margin = new Thickness(0, 0, 6, 0) };
        var record = Button("录入"); var saveKey = Button("保存换弹键");
        keyRow.Children.Add(key); keyRow.Children.Add(record); keyRow.Children.Add(saveKey); Children.Add(keyRow);
        bool recording = false;
        record.Click += (_, _) => { c.Reload.Cancel("正在录入换弹键"); recording = true; c.IsRecordingHotkey = true; key.Text = "…"; key.Focus(); };
        key.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            if (pressed == Key.Escape) key.Text = settings.TriggerKey;
            else { var name = pressed >= Key.D0 && pressed <= Key.D9 ? ((int)(pressed - Key.D0)).ToString() : pressed.ToString().ToUpperInvariant(); if (PzhReloadSettings.TryVirtualKey(name, out int unused)) key.Text = name; }
            recording = false; c.IsRecordingHotkey = false;
        };
        key.LostKeyboardFocus += (_, _) => { if (recording) { recording = false; c.IsRecordingHotkey = false; key.Text = settings.TriggerKey; } };
        saveKey.Click += (_, _) =>
        {
            var name = key.Text.Trim().ToUpperInvariant();
            if (!PzhReloadSettings.TryVirtualKey(name, out _)) { c.Notify("换弹键只支持单个字母或数字"); key.Text = settings.TriggerKey; return; }
            settings.TriggerKey = name; c.Reload.RefreshHook(); c.Save();
        };
        var region = Text($"识别区域：{settings.RegionX:P1}, {settings.RegionY:P1}, {settings.RegionWidth:P1} × {settings.RegionHeight:P1}");
        Children.Add(region);
        var regionRow = new WrapPanel();
        var select = Button("框选识别区域"); var reset = Button("恢复默认区域"); var test = Button("测试识别");
        regionRow.Children.Add(select); regionRow.Children.Add(reset); regionRow.Children.Add(test); Children.Add(regionRow);
        void SelectRegion(System.Drawing.Rectangle bounds)
        {
            bool saved = false;
            try
            {
                var picker = new PzhQteRegionWindow(bounds, settings.Copy());
                if (picker.ShowDialog() == true && picker.Selected is { } chosen)
                {
                    settings.RegionX = chosen.X; settings.RegionY = chosen.Y; settings.RegionWidth = chosen.Width; settings.RegionHeight = chosen.Height;
                    settings.Normalize(); c.Save();
                    region.Text = $"识别区域：{settings.RegionX:P1}, {settings.RegionY:P1}, {settings.RegionWidth:P1} × {settings.RegionHeight:P1}";
                    saved = true;
                }
            }
            finally { c.Reload.FinishRegionSelection(saved); }
        }
        select.Click += (_, _) => c.Reload.ArmRegionSelection(SelectRegion);
        reset.Click += (_, _) => { c.Reload.Cancel("识别区域已修改"); settings.ResetRegion(); c.Save(); region.Text = $"识别区域：{settings.RegionX:P1}, {settings.RegionY:P1}, {settings.RegionWidth:P1} × {settings.RegionHeight:P1}"; };
        test.Click += (_, _) => c.Reload.ArmRecognitionTest();
        Children.Add(Text("每次最多提交组数（1–8；提交不等于游戏确认成功）"));
        var countRow = new WrapPanel(); var count = new TextBox { Text = settings.GroupLimit.ToString(), Width = 65, Padding = new Thickness(6) }; var saveCount = Button("保存组数");
        countRow.Children.Add(count); countRow.Children.Add(saveCount); Children.Add(countRow);
        saveCount.Click += (_, _) => { if (!int.TryParse(count.Text, out int value) || value is < 1 or > 8) { c.Notify("组数须为 1–8"); return; } c.Reload.Cancel("组数已修改"); settings.GroupLimit = value; c.Save(); };
        var advanced = new Expander { Header = "高级时间参数（毫秒）", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 8) };
        var advancedBody = new StackPanel(); advanced.Content = advancedBody; Children.Add(advanced);
        var fields = new List<(string Label, TextBox Box, Action<int> Apply, int Min, int Max)>();
        void Field(string label, int value, Action<int> apply, int min, int max)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var box = new TextBox { Text = value.ToString(), Width = 70, Padding = new Thickness(4) };
            DockPanel.SetDock(box, Dock.Right); row.Children.Add(box); row.Children.Add(Text(label)); advancedBody.Children.Add(row);
            fields.Add((label, box, apply, min, max));
        }
        Field("按住时长", settings.KeyHoldMs, v => settings.KeyHoldMs = v, 15, 100);
        Field("按键间隔", settings.KeyGapMs, v => settings.KeyGapMs = v, 15, 150);
        Field("扫描间隔", settings.ScanIntervalMs, v => settings.ScanIntervalMs = v, 16, 100);
        Field("首次等待上限", settings.StartTimeoutMs, v => settings.StartTimeoutMs = v, 1000, 15000);
        Field("换组等待上限", settings.TransitionTimeoutMs, v => settings.TransitionTimeoutMs = v, 500, 5000);
        Field("任务总时限", settings.SessionTimeoutMs, v => settings.SessionTimeoutMs = v, 5000, 60000);
        var saveAdvanced = Button("保存高级参数"); advancedBody.Children.Add(saveAdvanced);
        saveAdvanced.Click += (_, _) =>
        {
            var values = new List<int>();
            foreach (var field in fields)
            {
                if (!int.TryParse(field.Box.Text, out int value) || value < field.Min || value > field.Max) { c.Notify($"{field.Label}须为 {field.Min}–{field.Max} 毫秒"); return; }
                values.Add(value);
            }
            c.Reload.Cancel("时间参数已修改");
            for (int i = 0; i < fields.Count; i++) fields[i].Apply(values[i]);
            c.Save();
        };
        var status = Text(""); status.Foreground = Brushes.White; Children.Add(status);
        void Refresh() => status.Text = $"状态：{c.Reload.Status} · 最近识别：{c.Reload.LastSequence}";
        Loaded += (_, _) => { c.Reload.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { c.Reload.Changed -= Refresh; recording = false; c.IsRecordingHotkey = false; };
    }
}
