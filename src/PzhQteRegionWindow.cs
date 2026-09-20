using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;

namespace WarDogs;

public sealed class PzhQteRegionWindow : Window
{
    readonly System.Drawing.Rectangle client;
    readonly Canvas canvas = new();
    readonly System.Windows.Shapes.Rectangle selection = new() { Stroke = Brushes.Cyan, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(55, 0, 220, 255)) };
    System.Windows.Point? drag;
    public Rect? Selected { get; private set; }

    public PzhQteRegionWindow(System.Drawing.Rectangle client, PzhReloadSettings current)
    {
        this.client = client;
        Title = "PZH QTE 识别区域"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
        Content = canvas; canvas.Children.Add(selection);
        var hint = new TextBlock { Text = "拖动框选四个箭头 · Enter 保存 · Esc 取消", Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(8) };
        canvas.Children.Add(hint); Canvas.SetLeft(hint, 8); Canvas.SetTop(hint, 8);
        Loaded += (_, _) =>
        {
            var source = PresentationSource.FromVisual(this);
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = fromDevice.Transform(new System.Windows.Point(client.Left, client.Top));
            var bottomRight = fromDevice.Transform(new System.Windows.Point(client.Right, client.Bottom));
            Left = topLeft.X; Top = topLeft.Y; Width = bottomRight.X - topLeft.X; Height = bottomRight.Y - topLeft.Y;
            UpdateLayout();
            Draw(new Rect(current.RegionX * ActualWidth, current.RegionY * ActualHeight, current.RegionWidth * ActualWidth, current.RegionHeight * ActualHeight));
            Focus();
        };
        PreviewMouseLeftButtonDown += (_, e) => { drag = e.GetPosition(canvas); CaptureMouse(); Draw(new Rect(drag.Value, drag.Value)); };
        PreviewMouseMove += (_, e) => { if (drag is { } start) Draw(new Rect(start, e.GetPosition(canvas))); };
        PreviewMouseLeftButtonUp += (_, e) => { if (drag is { } start) { drag = null; ReleaseMouseCapture(); Draw(new Rect(start, e.GetPosition(canvas))); } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == Key.Enter)
            {
                var r = new Rect(Canvas.GetLeft(selection), Canvas.GetTop(selection), selection.Width, selection.Height);
                if (r.Width >= 40 && r.Height >= 12 && r.Right <= ActualWidth && r.Bottom <= ActualHeight && r.Width * r.Height <= ActualWidth * ActualHeight * .15)
                { Selected = new Rect(r.X / ActualWidth, r.Y / ActualHeight, r.Width / ActualWidth, r.Height / ActualHeight); DialogResult = true; }
                else MessageBox.Show(this, "区域至少 40×12 像素，且不能超过客户区 15%。", "PZH QTE");
                e.Handled = true;
            }
        };
    }
    void Draw(Rect rect)
    {
        var clipped = Rect.Intersect(rect, new Rect(0, 0, ActualWidth, ActualHeight));
        if (clipped.IsEmpty) return;
        Canvas.SetLeft(selection, clipped.X); Canvas.SetTop(selection, clipped.Y);
        selection.Width = clipped.Width; selection.Height = clipped.Height;
    }
}
