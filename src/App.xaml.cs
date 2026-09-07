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
        instance = new SingleInstance(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Control?.Hud is {} hud) { hud.Show(); hud.Activate(); Control.Notify("软件已在运行 · 已显示悬浮窗"); }
        })));
        if (!instance.IsOwner) { Shutdown(); return; }
        DispatcherUnhandledException+=(s,a)=>{if(Control!=null){Control.Notify("操作未完成 · "+a.Exception.GetType().Name);a.Handled=true;}};
        Control=new Controller(e.Args.Contains("--demo")||e.Args.Contains("--verify"));
        var main=new MainWindow(Control);MainWindow=main;Control.Main=main;
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        Control.Hud=new HudWindow(Control);Control.Hud.Show();
        tray=new TrayIcon(Control);Control.InitializeNative(main);Control.Refresh();
        if(!Control.Demo) _ = InitializeUpdatesAsync();
        if(e.Args.Contains("--verify")){main.Show();Dispatcher.BeginInvoke(new Action(async()=>{await main.VerifyAndCapture();if(e.Args.Contains("--verify-exit"))Control.Quit();}));}
    }
    async Task InitializeUpdatesAsync()
    {
        await Control.Updates.RestoreAsync();
        if (Control.Pref.AutoCheckUpdates) await Control.Updates.CheckAsync();
    }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose();instance?.Dispose(); base.OnExit(e); }
}
