using System.Windows;
using System.IO;
namespace WarDogs;
public partial class App:Application
{
    internal static Controller Control=null!;
    SingleInstance? instance; TrayIcon? tray;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var verify=e.Args.Contains("--verify");
        var verifyMultiplayer=e.Args.Contains("--verify-multiplayer");
        instance = new SingleInstance(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Control?.Hud is {} hud) { hud.Show(); hud.Activate(); Control.Notify("软件已在运行 · 已显示悬浮窗"); }
        })), verify||verifyMultiplayer ? @"Local\WarDogsOverlay.Verify."+Environment.ProcessId : @"Local\WarDogsOverlay.Instance");
        if (!instance.IsOwner) { Shutdown(); return; }
        DispatcherUnhandledException+=(s,a)=>{if(Control!=null){Control.Notify("操作未完成 · "+a.Exception.GetType().Name);a.Handled=true;}};
        Control=new Controller(e.Args.Contains("--demo")||verify||verifyMultiplayer);
        var main=new MainWindow(Control);MainWindow=main;Control.Main=main;
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        Control.Hud=new HudWindow(Control);Control.Hud.Show();
        tray=new TrayIcon(Control);Control.InitializeNative(main,!verifyMultiplayer);Control.Refresh();
        if(!Control.Demo) _ = InitializeUpdatesAsync();
        if(!Control.Demo&&Control.Pref.EnableTestFeatures&&Control.Pref.Multiplayer.AutoJoin) _ = Control.Rooms.JoinAsync();
        if(verifyMultiplayer)Dispatcher.BeginInvoke(new Action(async()=>{await WarDogs.Multiplayer.MultiplayerVerification.RunAsync(Control,e.Args.FirstOrDefault(a=>a.StartsWith("--server="))?[9..]);Control.Quit();}));
        if(verify){main.Show();Dispatcher.BeginInvoke(new Action(async()=>{await main.VerifyAndCapture();if(e.Args.Contains("--verify-exit"))Control.Quit();}));}
    }
    async Task InitializeUpdatesAsync()
    {
        await Control.Updates.RestoreAsync();
        if (Control.Pref.AutoCheckUpdates) await Control.Updates.CheckAsync();
    }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose();instance?.Dispose(); base.OnExit(e); }
}
