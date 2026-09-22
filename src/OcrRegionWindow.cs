using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WarDogs;

public sealed class OcrRegionWindow:Window
{
    readonly Canvas canvas=new();readonly Border selection=new(){BorderBrush=Brushes.DeepSkyBlue,BorderThickness=new Thickness(3),Background=new SolidColorBrush(Color.FromArgb(35,0,190,255))};
    System.Windows.Point? drag;public Rect? Selected{get;private set;}
    public OcrRegionWindow(System.Drawing.Rectangle client,Rect current)
    {
        Title="DeepSeek OCR 地图区域";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=new SolidColorBrush(Color.FromArgb(45,0,0,0));Topmost=true;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.Manual;Content=canvas;canvas.Children.Add(selection);
        var hint=new TextBlock{Text="拖动框选完整地图 · Enter 保存 · Esc 取消",Foreground=Brushes.White,Background=Brushes.Black,Padding=new Thickness(8)};canvas.Children.Add(hint);Canvas.SetLeft(hint,8);Canvas.SetTop(hint,8);
        Loaded+=(_,_)=>{var source=PresentationSource.FromVisual(this);var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;var a=transform.Transform(new System.Windows.Point(client.Left,client.Top));var b=transform.Transform(new System.Windows.Point(client.Right,client.Bottom));Left=a.X;Top=a.Y;Width=b.X-a.X;Height=b.Y-a.Y;UpdateLayout();Draw(new Rect(current.X*ActualWidth,current.Y*ActualHeight,current.Width*ActualWidth,current.Height*ActualHeight));Focus();};
        PreviewMouseLeftButtonDown+=(_,e)=>{drag=e.GetPosition(canvas);CaptureMouse();Draw(new Rect(drag.Value,drag.Value));};PreviewMouseMove+=(_,e)=>{if(drag is {} start)Draw(new Rect(start,e.GetPosition(canvas)));};PreviewMouseLeftButtonUp+=(_,e)=>{if(drag is {} start){drag=null;ReleaseMouseCapture();Draw(new Rect(start,e.GetPosition(canvas)));}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){DialogResult=false;e.Handled=true;}else if(e.Key==Key.Enter){var r=new Rect(Canvas.GetLeft(selection),Canvas.GetTop(selection),selection.Width,selection.Height);if(r.Width>=100&&r.Height>=100&&r.Right<=ActualWidth&&r.Bottom<=ActualHeight){Selected=new(r.X/ActualWidth,r.Y/ActualHeight,r.Width/ActualWidth,r.Height/ActualHeight);DialogResult=true;}else MessageBox.Show(this,"请框选至少 100×100 像素的完整地图。","DeepSeek OCR");e.Handled=true;}};
    }
    void Draw(Rect value){var r=Rect.Intersect(value,new Rect(0,0,ActualWidth,ActualHeight));if(r.IsEmpty)return;Canvas.SetLeft(selection,r.X);Canvas.SetTop(selection,r.Y);selection.Width=r.Width;selection.Height=r.Height;}
}
