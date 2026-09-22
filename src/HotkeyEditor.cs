using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WarDogs;

public sealed class HotkeyEditor:DockPanel
{
    readonly Controller owner;readonly string action;readonly TextBox box;bool recording;
    public HotkeyEditor(Controller owner,string action)
    {
        this.owner=owner;this.action=action;Margin=new Thickness(0,0,0,5);
        Button Small(string text)=>new(){Content=text,Padding=new Thickness(7,5,7,5),Margin=new Thickness(4,0,0,0),FontSize=11};
        var save=Small("保存");SetDock(save,Dock.Right);Children.Add(save);var record=Small("录入");SetDock(record,Dock.Right);Children.Add(record);var clear=Small("清除");SetDock(clear,Dock.Right);Children.Add(clear);box=new(){Text=Value(),FontSize=11,Padding=new Thickness(7,5,7,5),MinWidth=90};Children.Add(box);
        record.Click+=(_,_)=>{recording=true;owner.IsRecordingHotkey=true;box.Text="请按快捷键…";box.Focus();};
        box.PreviewKeyDown+=(_,e)=>{if(!recording)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;if(key==Key.Escape)box.Text=Value();else{try{box.Text=new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key,Keyboard.Modifiers))??"";}catch{box.Text="";owner.Notify("请使用修饰键组合，或 F1–F24");}}recording=false;owner.IsRecordingHotkey=false;};
        box.LostKeyboardFocus+=(_,_)=>{if(recording){recording=false;owner.IsRecordingHotkey=false;box.Text=Value();}};clear.Click+=(_,_)=>{if(owner.Bind(action,""))box.Text="";};save.Click+=(_,_)=>{if(!owner.Bind(action,box.Text))box.Text=Value();};
        Loaded+=(_,_)=>{box.Text=Value();owner.HotkeysChanged+=Refresh;owner.HotkeyRecorded+=Recorded;};Unloaded+=(_,_)=>{owner.HotkeysChanged-=Refresh;owner.HotkeyRecorded-=Recorded;recording=false;owner.IsRecordingHotkey=false;};
    }
    string Value()=>owner.Pref.Keys.GetValueOrDefault(action,"");void Refresh(){if(!recording)box.Text=Value();}void Recorded(string gesture){if(recording){box.Text=gesture;recording=false;owner.IsRecordingHotkey=false;}}
}
