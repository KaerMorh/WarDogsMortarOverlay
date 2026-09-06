using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace WarDogs;
public class HistoryPanel:ListBox
{
    public HistoryPanel(Controller c)
    {
        ItemsSource=c.History;Background=new SolidColorBrush(Color.FromRgb(20,25,35));BorderThickness=new Thickness(0);
        HorizontalContentAlignment=HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(this,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(this,true);VirtualizingPanel.SetVirtualizationMode(this,VirtualizationMode.Recycling);
        var itemStyle=new Style(typeof(ListBoxItem));itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));itemStyle.Setters.Add(new Setter(PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(0)));ItemContainerStyle=itemStyle;
        var button=new FrameworkElementFactory(typeof(Button));button.SetValue(PaddingProperty,new Thickness(8));button.SetValue(MarginProperty,new Thickness(0,0,2,4));button.SetValue(HorizontalContentAlignmentProperty,HorizontalAlignment.Left);
        button.SetBinding(ToolTipProperty,new Binding(nameof(HistoryEntry.Hint)));
        button.AddHandler(Button.ClickEvent,new RoutedEventHandler((s,e)=>{if(s is FrameworkElement {DataContext:HistoryEntry h})c.RestoreHistory(h);}));
        var stack=new FrameworkElementFactory(typeof(StackPanel));
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding(nameof(HistoryEntry.Title)));title.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(HistoryEntry.Color)));title.SetValue(TextBlock.FontSizeProperty,11d);title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);stack.AppendChild(title);
        var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding(nameof(HistoryEntry.Detail)));detail.SetValue(TextBlock.ForegroundProperty,new SolidColorBrush(Color.FromRgb(218,226,237)));detail.SetValue(TextBlock.FontSizeProperty,10d);detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);stack.AppendChild(detail);
        button.AppendChild(stack);ItemTemplate=new DataTemplate{VisualTree=button};
    }
}
