using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WarDogs.Qte;

namespace WarDogs;

public sealed class PzhReloadSettings
{
    public bool Enabled { get; set; }
    public string TriggerKey { get; set; } = "R";
    public double RegionX { get; set; } = .455;
    public double RegionY { get; set; } = .557;
    public double RegionWidth { get; set; } = .09;
    public double RegionHeight { get; set; } = .042;
    public int GroupLimit { get; set; } = 4;
    public int KeyHoldMs { get; set; } = 30;
    public int KeyGapMs { get; set; } = 40;
    public int ScanIntervalMs { get; set; } = 50;
    public int StartTimeoutMs { get; set; } = 5000;
    public int TransitionTimeoutMs { get; set; } = 2000;
    public int SessionTimeoutMs { get; set; } = 20000;

    public void Normalize()
    {
        if (!double.IsFinite(RegionX) || !double.IsFinite(RegionY) || !double.IsFinite(RegionWidth) || !double.IsFinite(RegionHeight) ||
            RegionX < 0 || RegionY < 0 || RegionWidth <= 0 || RegionHeight <= 0 || RegionX + RegionWidth > 1 || RegionY + RegionHeight > 1 || RegionWidth * RegionHeight > .15)
            ResetRegion();
        if (!TryVirtualKey(TriggerKey, out _)) TriggerKey = "R";
        GroupLimit = Math.Clamp(GroupLimit, 1, 8);
        KeyHoldMs = Math.Clamp(KeyHoldMs, 15, 100);
        KeyGapMs = Math.Clamp(KeyGapMs, 15, 150);
        ScanIntervalMs = Math.Clamp(ScanIntervalMs, 16, 100);
        StartTimeoutMs = Math.Clamp(StartTimeoutMs, 1000, 15000);
        TransitionTimeoutMs = Math.Clamp(TransitionTimeoutMs, 500, 5000);
        SessionTimeoutMs = Math.Clamp(SessionTimeoutMs, 5000, 60000);
    }
    public void ResetRegion() { RegionX = .455; RegionY = .557; RegionWidth = .09; RegionHeight = .042; }
    public PzhReloadSettings Copy() => (PzhReloadSettings)MemberwiseClone();
    public static bool TryVirtualKey(string? name, out int vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(name) || name.Length != 1) return false;
        char ch = char.ToUpperInvariant(name[0]);
        if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = ch; return true; }
        return false;
    }
}

public enum PzhReloadState { Idle, WaitingForQte, Sending, WaitingForTransition }

public sealed class PzhReloadService : IDisposable
{
    readonly Controller owner;
    readonly Dispatcher ui;
    readonly object gate = new();
    CancellationTokenSource? session;
    Task? running;
    int sessionVersion;
    string cancellationReason = "已取消";
    IntPtr hook;
    HookCallback? callback;
    bool triggerDown;
    bool testArmed, regionArmed;
    Action<Rectangle>? regionCallback;
    int heldKey;
    public bool Selecting { get; set; }
    public PzhReloadState State { get; private set; }
    public string Status { get; private set; } = "未启用";
    public string LastSequence { get; private set; } = "—";
    public event Action? Changed;

    public PzhReloadService(Controller controller)
    {
        owner = controller;
        ui = Application.Current.Dispatcher;
    }
    void Update(PzhReloadState state, string status)
    {
        if (ui.HasShutdownStarted || ui.HasShutdownFinished) return;
        ui.BeginInvoke(new Action(() => { State = state; Status = status; Changed?.Invoke(); }));
    }
    public void RefreshHook()
    {
        Cancel("设置已更改");
        InstallHook();
    }
    void InstallHook()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        if (!AllowedSettings && !testArmed && !regionArmed) { triggerDown = false; Update(PzhReloadState.Idle, "未启用"); return; }
        PzhReloadSettings.TryVirtualKey(owner.Pref.PzhReload.TriggerKey, out int trigger);
        triggerDown = (GetAsyncKeyState(trigger) & 0x8000) != 0;
        callback = OnKeyboard;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        Update(PzhReloadState.Idle, hook == IntPtr.Zero ? "键盘监听启动失败" : testArmed ? "测试待命 · 在游戏中按换弹键" : regionArmed ? "框选待命 · 在游戏中按换弹键" : "就绪 · 等待换弹键");
    }
    bool AllowedSettings => !owner.Demo && !owner.IsClosing && owner.Pref.PzhReload.Enabled;
    bool Ready => AllowedSettings && owner.State.Weapon == "spg" && !owner.State.Paused && !owner.IsRecordingHotkey && !Selecting && owner.TaskPicker?.IsVisible != true;
    public void CheckConditions()
    {
        if (!Ready) Cancel("模式、暂停或界面状态已改变");
    }
    public void ArmRecognitionTest()
    {
        Cancel("开始测试识别");
        regionCallback = null;
        testArmed = true; regionArmed = false;
        InstallHook();
    }
    public void ArmRegionSelection(Action<Rectangle> onSelect)
    {
        Cancel("开始框选");
        regionCallback = onSelect;
        regionArmed = true; testArmed = false;
        InstallHook();
    }
    void EndOneShot()
    {
        testArmed = regionArmed = false;
        if (!AllowedSettings && hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
    public void FinishRegionSelection(bool saved)
    {
        Selecting = false;
        Update(PzhReloadState.Idle, saved ? "识别区域已保存" : "已取消框选");
    }
    IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdHook>(data);
            if ((info.flags & 0x10) == 0)
            {
                bool down = message == (IntPtr)0x100 || message == (IntPtr)0x104;
                bool up = message == (IntPtr)0x101 || message == (IntPtr)0x105;
                PzhReloadSettings.TryVirtualKey(owner.Pref.PzhReload.TriggerKey, out int trigger);
                if (info.vkCode == trigger)
                {
                    if (down && !triggerDown) { triggerDown = true; ui.BeginInvoke(new Action(TryStart)); }
                    if (up) triggerDown = false;
                }
                if (down && (info.vkCode == 0x1B || info.vkCode is >= 0x25 and <= 0x28))
                    ui.BeginInvoke(new Action(() => Cancel(info.vkCode == 0x1B ? "用户按 Esc 取消" : "用户接管方向键")));
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    void TryStart()
    {
        if (hook == IntPtr.Zero || owner.IsRecordingHotkey || Selecting) return;
        bool test = testArmed, select = regionArmed;
        if (!test && !select && !Ready) return;
        var settings = owner.Pref.PzhReload.Copy();
        if (!test && !select) InterruptForNewTrigger();
        if (!TryGetForegroundTarget(out var target, out var bounds, out var reason))
        {
            if (test || select) Update(PzhReloadState.Idle, "等待在游戏窗口按换弹键");
            return;
        }
        if (select)
        {
            var callback = regionCallback;
            regionCallback = null;
            EndOneShot(); Selecting = true;
            Update(PzhReloadState.Idle, "正在框选识别区域");
            if (callback != null) callback(bounds); else Selecting = false;
            return;
        }
        if (!test && !Ready) return;
        GetWindowThreadProcessId(target, out uint processId);
        if (test) EndOneShot();
        StartSession(token => test ? TestAsync(target, processId, settings, token) : RunAsync(target, processId, settings, token),
            test ? "重新开始测试识别" : "已由新的换弹操作重启");
    }
    void InterruptForNewTrigger()
    {
        lock (gate)
        {
            cancellationReason = "已由新的换弹操作重启";
            sessionVersion++;
            session?.Cancel();
        }
        ReleaseHeld();
    }
    void StartSession(Func<CancellationToken, Task> work, string restartReason)
    {
        Task? previous;
        int version;
        lock (gate)
        {
            previous = running;
            cancellationReason = restartReason;
            session?.Cancel();
            version = ++sessionVersion;
            running = StartAfterPreviousAsync(previous, version, work);
        }
    }
    async Task StartAfterPreviousAsync(Task? previous, int version, Func<CancellationToken, Task> work)
    {
        await Task.Yield();
        if (previous != null) try { await previous; } catch { }
        CancellationTokenSource next;
        lock (gate)
        {
            if (version != sessionVersion) return;
            session?.Dispose();
            next = new CancellationTokenSource();
            session = next;
            cancellationReason = "已取消";
        }
        await Task.Run(() => work(next.Token));
    }
    async Task TestAsync(IntPtr target, uint processId, PzhReloadSettings settings, CancellationToken token)
    {
        string stop = "测试识别已取消", previous = "";
        int stable = 0;
        double lastFrameMs = 0;
        var elapsed = Stopwatch.StartNew();
        var frameWatch = new Stopwatch();
        try
        {
            Update(PzhReloadState.WaitingForQte, "测试中 · 等待 QTE");
            while (elapsed.ElapsedMilliseconds < settings.StartTimeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (!SameForeground(target, processId)) { stop = "测试识别：窗口失去焦点"; break; }
                frameWatch.Restart();
                if (!TryCapture(target, settings, out var bitmap, out int height, out var error)) { stop = error; break; }
                Recognition result;
                using (bitmap) result = QteRecognizer.RecognizeRegion(bitmap, height);
                frameWatch.Stop(); lastFrameMs = frameWatch.Elapsed.TotalMilliseconds;
                if (result.Accepted)
                {
                    ReportSequence(result.Sequence);
                    stable = result.Sequence == previous ? stable + 1 : 1;
                    previous = result.Sequence;
                    if (stable >= 2) { stop = $"测试识别：{result.Sequence} · 截图＋识别 {lastFrameMs:F1}ms（未发送按键）"; break; }
                }
                else { stable = 0; previous = ""; }
                await Task.Delay(settings.ScanIntervalMs, token);
            }
            if (elapsed.ElapsedMilliseconds >= settings.StartTimeoutMs) stop = "测试识别：未检测到可信 QTE";
        }
        catch (OperationCanceledException) { lock (gate) stop = cancellationReason; }
        catch (Exception ex) { stop = $"测试识别失败：{ex.GetType().Name}"; }
        finally { Update(PzhReloadState.Idle, stop); }
    }
    void ReportSequence(string sequence)
    {
        _ = ui.BeginInvoke(new Action(() => { if (LastSequence != sequence) { LastSequence = sequence; Changed?.Invoke(); } }));
    }
    public void Cancel(string reason)
    {
        bool wasArmed = testArmed || regionArmed;
        testArmed = regionArmed = false;
        regionCallback = null;
        lock (gate) { cancellationReason = reason; sessionVersion++; session?.Cancel(); }
        if (!AllowedSettings && hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        if (State != PzhReloadState.Idle || wasArmed) Update(PzhReloadState.Idle, reason);
    }
    async Task RunAsync(IntPtr target, uint processId, PzhReloadSettings settings, CancellationToken token)
    {
        int groups = 0;
        string candidate = "", stop = "已取消";
        Rectangle[]? candidateBounds = null;
        int stable = 0;
        var started = Stopwatch.StartNew();
        var phase = Stopwatch.StartNew();
        try
        {
            Update(PzhReloadState.WaitingForQte, "等待 QTE");
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (started.ElapsedMilliseconds > settings.SessionTimeoutMs) { stop = "任务总超时"; break; }
                if (!ReadyOnUi()) { stop = "模式、暂停或界面状态已改变"; break; }
                if (!SameForeground(target, processId)) { stop = "目标窗口失去前台焦点"; break; }
                if (!TryCapture(target, settings, out var bitmap, out int height, out var error)) { stop = error; break; }
                Recognition result;
                using (bitmap) result = QteRecognizer.RecognizeRegion(bitmap, height);
                if (result.Accepted) ReportSequence(result.Sequence);
                // Only a stable set of four white arrows is accepted. Pressed green/red arrows
                // drop out naturally; the next white group may repeat the same directions.
                if (result.Accepted)
                {
                    var bounds = result.Arrows.Select(a => a.Bounds).ToArray();
                    if (candidate == result.Sequence && candidateBounds != null && bounds.Zip(candidateBounds).All(pair => Math.Abs(pair.First.X - pair.Second.X) <= 4 && Math.Abs(pair.First.Y - pair.Second.Y) <= 4)) stable++;
                    else { candidate = result.Sequence; candidateBounds = bounds; stable = 1; }
                    if (stable >= 2)
                    {
                        if (groups > 0) Update(PzhReloadState.WaitingForQte, "已检测到下一组");
                        Update(PzhReloadState.Sending, $"正在输入第 {groups + 1} 组");
                        foreach (char direction in candidate)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!ReadyOnUi() || !SameForeground(target, processId)) throw new OperationCanceledException("游戏焦点或设置已改变");
                            await TapAsync(direction, settings.KeyHoldMs, token);
                            await Task.Delay(settings.KeyGapMs, token);
                        }
                        groups++;
                        candidate = ""; candidateBounds = null; stable = 0;
                        if (groups >= settings.GroupLimit) { stop = $"已提交 {groups} 组（未确认装弹成功）"; break; }
                        phase.Restart();
                        Update(PzhReloadState.WaitingForTransition, $"已提交 {groups} 组 · 等待换组");
                    }
                }
                else { candidate = ""; candidateBounds = null; stable = 0; }
                if (groups == 0 && phase.ElapsedMilliseconds > settings.StartTimeoutMs) { stop = "未检测到可信 QTE"; break; }
                if (groups > 0 && phase.ElapsedMilliseconds > settings.TransitionTimeoutMs) { stop = "未确认换组"; break; }
                await Task.Delay(settings.ScanIntervalMs, token);
            }
        }
        catch (OperationCanceledException) { lock (gate) stop = cancellationReason; }
        catch (Exception ex) { stop = $"执行失败：{ex.GetType().Name}"; }
        finally
        {
            ReleaseHeld();
            Update(PzhReloadState.Idle, stop);
        }
    }
    bool ReadyOnUi()
    {
        bool ready = false;
        ui.Invoke(() => ready = Ready);
        return ready;
    }
    static bool SameForeground(IntPtr target, uint processId) => GetForegroundWindow() == target && GetWindowThreadProcessId(target, out uint current) != 0 && current == processId && !IsIconic(target);
    async Task TapAsync(char direction, int holdMs, CancellationToken token)
    {
        int key = direction switch { '左' => 0x25, '上' => 0x26, '右' => 0x27, '下' => 0x28, _ => throw new InvalidOperationException("未知方向") };
        int scan = (int)MapVirtualKey((uint)key, 0);
        if (!SendScan(scan, false)) throw new InvalidOperationException("SendInput 按下失败");
        Volatile.Write(ref heldKey, scan);
        try { await Task.Delay(holdMs, token); }
        finally { ReleaseHeld(); }
    }
    void ReleaseHeld()
    {
        int scan = Interlocked.Exchange(ref heldKey, 0);
        if (scan != 0) SendScan(scan, true);
    }
    static bool SendScan(int scan, bool up)
    {
        var input = new INPUT { type = 1, ki = new KEYBDINPUT { wScan = (ushort)scan, dwFlags = (uint)(0x0008 | 0x0001 | (up ? 0x0002 : 0)) } };
        return SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
    }
    public bool TryGetForegroundTarget(out IntPtr hwnd, out Rectangle bounds, out string reason)
    {
        hwnd = GetForegroundWindow(); bounds = Rectangle.Empty; reason = "";
        if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == Environment.ProcessId) { reason = "前台不是游戏窗口"; return false; }
        if (IsIconic(hwnd) || !GetClientRect(hwnd, out var rect) || rect.Right < 100 || rect.Bottom < 100) { reason = "游戏窗口不可截图"; return false; }
        if (GetDpiForWindow(hwnd) != GetDpiForSystem()) { reason = "游戏窗口与应用 DPI 不一致；请移到主显示器"; return false; }
        var point = new POINT(); if (!ClientToScreen(hwnd, ref point)) { reason = "无法定位游戏客户区"; return false; }
        bounds = new Rectangle(point.X, point.Y, rect.Right, rect.Bottom);
        return true;
    }
    public bool TryCapture(IntPtr hwnd, PzhReloadSettings settings, out Bitmap bitmap, out int clientHeight, out string error)
    {
        bitmap = null!; clientHeight = 0; error = "";
        if (IsIconic(hwnd) || !GetClientRect(hwnd, out var client) || client.Right < 100 || client.Bottom < 100) { error = "游戏客户区不可用"; return false; }
        var point = new POINT();
        if (!ClientToScreen(hwnd, ref point)) { error = "无法定位游戏客户区"; return false; }
        int x = (int)Math.Round(client.Right * settings.RegionX), y = (int)Math.Round(client.Bottom * settings.RegionY);
        int width = (int)Math.Round(client.Right * settings.RegionWidth), height = (int)Math.Round(client.Bottom * settings.RegionHeight);
        if (width < 40 || height < 12 || x < 0 || y < 0 || x + width > client.Right || y + height > client.Bottom) { error = "识别区域超出游戏客户区"; return false; }
        try
        {
            bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(point.X + x, point.Y + y, 0, 0, new System.Drawing.Size(width, height));
            clientHeight = client.Bottom;
            return true;
        }
        catch { bitmap?.Dispose(); error = "区域截图失败"; return false; }
    }
    public void Dispose() { Cancel("程序退出"); regionCallback = null; if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } ReleaseHeld(); }

    delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct KbdHook { public int vkCode, scanCode, flags, time; public IntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public long padding; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
