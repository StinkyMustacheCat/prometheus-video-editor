using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prometheus {
 public partial class MainForm {
  const int ShelfHotkeyId=0x7051;
  const int WM_HOTKEY=0x0312;
  bool shelfHotkeyRegistered;
  IntPtr shelfCaptureWindow;
  uint shelfClipboardSequence;
  readonly System.Windows.Forms.Timer shelfCaptureTimer=new System.Windows.Forms.Timer{Interval=35};
  int shelfCaptureTicks;
  [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
  [DllImport("user32.dll",SetLastError=true)]static extern bool UnregisterHotKey(IntPtr window,int id);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber();
  [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
  void RegisterShelfHotkey(){
   UnregisterShelfHotkey();Keys value=shortcutMap.Get("shelf.capture");if(value==Keys.None||App.Capture)return;
   uint mods=0;if((value&Keys.Alt)!=0)mods|=1;if((value&Keys.Control)!=0)mods|=2;if((value&Keys.Shift)!=0)mods|=4;
   if(mods==0){status.Text="La captura global de Bandeja necesita Ctrl, Alt o Mayús";return;}
   shelfHotkeyRegistered=RegisterHotKey(Handle,ShelfHotkeyId,mods|0x4000,(uint)(value&Keys.KeyCode));
   if(!shelfHotkeyRegistered)MessageBox.Show(this,"Windows no pudo reservar "+ShortcutMap.Label(value)+" para la Bandeja. Está ocupado por otra aplicación; puedes cambiarlo en Atajos de teclado.","Atajo global ocupado",MessageBoxButtons.OK,MessageBoxIcon.Information);
   shelfCaptureTimer.Tick-=ShelfCaptureTick;shelfCaptureTimer.Tick+=ShelfCaptureTick;
  }
  void UnregisterShelfHotkey(){shelfCaptureTimer.Stop();if(shelfHotkeyRegistered&&IsHandleCreated)UnregisterHotKey(Handle,ShelfHotkeyId);shelfHotkeyRegistered=false;}
  protected override void WndProc(ref Message message){
   if(message.Msg==0x0112&&(message.WParam.ToInt64()&0xFFF0)==0xF100&&message.LParam==IntPtr.Zero)return;
   if(message.Msg==WM_HOTKEY&&message.WParam.ToInt32()==ShelfHotkeyId){
    shelfCaptureTimer.Stop();shelfCaptureTimer.Tick-=ShelfClipboardTick;shelfCaptureTimer.Tick-=ShelfCaptureTick;shelfCaptureTimer.Tick+=ShelfCaptureTick;
    shelfCaptureWindow=GetForegroundWindow();shelfCaptureTicks=0;shelfCaptureTimer.Start();return;
   }
   base.WndProc(ref message);
  }
  void ShelfCaptureTick(object sender,EventArgs args){
   if(++shelfCaptureTicks>90){shelfCaptureTimer.Stop();return;}
   if((GetAsyncKeyState(0x11)&0x8000)!=0||(GetAsyncKeyState(0x12)&0x8000)!=0||(GetAsyncKeyState(0x10)&0x8000)!=0)return;
   shelfCaptureTimer.Stop();if(shelfCaptureWindow==IntPtr.Zero||GetForegroundWindow()!=shelfCaptureWindow)return;
   shelfClipboardSequence=GetClipboardSequenceNumber();
   keybd_event(0x11,0,0,UIntPtr.Zero);keybd_event(0x43,0,0,UIntPtr.Zero);keybd_event(0x43,0,2,UIntPtr.Zero);keybd_event(0x11,0,2,UIntPtr.Zero);
   shelfCaptureTicks=0;shelfCaptureTimer.Start();shelfCaptureTimer.Tick-=ShelfCaptureTick;shelfCaptureTimer.Tick+=ShelfClipboardTick;
  }
  void ShelfClipboardTick(object sender,EventArgs args){
   if(++shelfCaptureTicks>30){shelfCaptureTimer.Stop();shelfCaptureTimer.Tick-=ShelfClipboardTick;return;}
   if(GetClipboardSequenceNumber()==shelfClipboardSequence)return;
   shelfCaptureTimer.Stop();shelfCaptureTimer.Tick-=ShelfClipboardTick;
   try{if(Clipboard.ContainsText())CaptureShelfText(Clipboard.GetText());}catch{status.Text="No se pudo leer el texto copiado para la Bandeja";}
  }
 }
}
