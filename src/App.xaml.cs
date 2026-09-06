using System.Windows;
using System.IO;
namespace WarDogs;
public partial class App:Application
{
    internal static Controller Control=null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException+=(s,a)=>{if(Control!=null){Control.Notify("操作未完成 · "+a.Exception.GetType().Name);a.Handled=true;}};
        Control=new Controller(e.Args.Contains("--demo")||e.Args.Contains("--verify"));
        var main=new MainWindow(Control);MainWindow=main;Control.Main=main;
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        Control.Hud=new HudWindow(Control);Control.Hud.Show();
        Control.InitializeNative(main);Control.Refresh();
        if(e.Args.Contains("--verify")){main.Show();Dispatcher.BeginInvoke(new Action(async()=>await main.VerifyAndCapture()));}
    }
}
