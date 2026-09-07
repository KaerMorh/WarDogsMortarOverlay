using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
namespace WarDogs;

internal sealed class TrayIcon : IDisposable
{
    readonly Controller controller;
    readonly Forms.NotifyIcon icon;
    readonly System.Drawing.Icon image;
    readonly DispatcherTimer clickTimer;
    ContextMenu? menu;
    public TrayIcon(Controller controller)
    {
        this.controller=controller;
        using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))!.Stream;
        image=new System.Drawing.Icon(stream,32,32);
        icon=new Forms.NotifyIcon { Icon=image,Text="WARDOGS · 单击显示悬浮窗 / 双击大地图",Visible=true };
        clickTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
        clickTimer.Tick+=SingleClick;
        icon.MouseClick+=MouseClick;
        icon.MouseDoubleClick+=DoubleClick;
    }
    void SingleClick(object? sender,EventArgs e){clickTimer.Stop();controller.Hud.ShowForm("panel");}
    void DoubleClick(object? sender,Forms.MouseEventArgs e)
    {
        if(e.Button!=Forms.MouseButtons.Left)return;
        clickTimer.Stop();controller.ShowMap();
    }
    void MouseClick(object? sender,Forms.MouseEventArgs e)
    {
        if(e.Button==Forms.MouseButtons.Left){clickTimer.Stop();clickTimer.Start();}
        else if(e.Button==Forms.MouseButtons.Right)
        {
            clickTimer.Stop();
            if(menu!=null)menu.IsOpen=false;
            SetForegroundWindow(new WindowInteropHelper(controller.Hud).Handle);
            menu=controller.Hud.BubbleMenu();
            menu.PlacementTarget=controller.Hud;
            menu.Placement=PlacementMode.MousePoint;
            menu.IsOpen=true;
        }
    }
    public void Dispose()
    {
        clickTimer.Stop();clickTimer.Tick-=SingleClick;
        if(menu!=null)menu.IsOpen=false;
        icon.Visible=false;icon.Dispose();image.Dispose();
    }
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
}
