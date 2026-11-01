using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prometheus {
 // Keep native scroll controls subdued on Windows versions that expose a dark Explorer theme.
 public static class DarkScrollTheme {
  static readonly HashSet<Control> attached=new HashSet<Control>();
  [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr handle,string subApp,string subId);
  public static void Attach(Control root){
   if(root==null)return;
   if(!attached.Add(root))return;
   root.Disposed+=(s,e)=>attached.Remove(root);
   root.HandleCreated+=(s,e)=>Apply(root);
   root.ControlAdded+=(s,e)=>Attach(e.Control);
   foreach(Control child in root.Controls)Attach(child);
   if(root.IsHandleCreated)Apply(root);
  }
  static void Apply(Control control){
   bool scrollable=control is ListBox||control is TreeView||control is ScrollableControl&&((ScrollableControl)control).AutoScroll;
   var text=control as TextBoxBase;
   if(text!=null&&text is TextBox&&((TextBox)text).ScrollBars!=ScrollBars.None)scrollable=true;
   if(!scrollable)return;
   try{SetWindowTheme(control.Handle,"DarkMode_Explorer",null);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
  }
 }
}
