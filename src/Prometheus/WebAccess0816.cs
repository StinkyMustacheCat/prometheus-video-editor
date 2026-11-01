using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class WebAuthBatchException:Exception {
  public readonly List<string> Urls;
  public WebAuthBatchException(IEnumerable<string> urls):base("Algunos sitios necesitan confirmar el acceso en el navegador."){Urls=urls.Distinct().ToList();}
 }
 public partial class MainForm {
  bool ShowWebAccessPrompt(IEnumerable<string> urls,IWin32Window owner=null){
   var sites=urls.Select(url=>{Uri uri;return Uri.TryCreate(url,UriKind.Absolute,out uri)?uri.Host:url;}).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
   var pages=urls.Select(url=>new Uri(url)).GroupBy(uri=>uri.Host,StringComparer.OrdinalIgnoreCase).Select(group=>group.First().AbsoluteUri).ToList();
   using(var dialog=new Form{Text="Acceso a medios web",ClientSize=new Size(540,286),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false,BackColor=panel,ForeColor=text,Font=new Font("Segoe UI",9)}){
    var heading=new Label{Text="Confirma el acceso una vez para reintentar el lote",Left=18,Top=15,Width=500,Height=29,Font=new Font("Segoe UI",12,FontStyle.Bold)};
    var domain=new Label{Text=String.Join(", ",sites),Left=18,Top=48,Width=500,Height=25,ForeColor=Color.FromArgb(255,199,126),AutoEllipsis=true};
    var note=new Label{Text="Confirma el acceso en el navegador y reintenta. Si el sitio vuelve a rechazarlo, elige otro navegador o un archivo de cookies.",Left=18,Top=80,Width=500,Height=48};
    var caption=new Label{Text="Usar cookies de",Left=18,Top=133,Width=172,Height=25};
    var browser=new ComboBox{Left=195,Top=130,Width=188,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=bg,ForeColor=text};browser.Items.AddRange(new object[]{"Chrome","Edge","Firefox","Archivo de cookies"});browser.SelectedIndex=!String.IsNullOrWhiteSpace(WebMedia.CookieFile)?3:WebMedia.CookieBrowser=="chrome"?0:WebMedia.CookieBrowser=="edge"?1:WebMedia.CookieBrowser=="firefox"?2:-1;
    var open=new Button{Text="Abrir sitio",Left=391,Top=129,Width=126,Height=29};
    open.Click+=(s,e)=>{if(browser.SelectedIndex<0||browser.SelectedIndex==3){MessageBox.Show(dialog,"Elige Chrome, Edge o Firefox para abrir el sitio.");return;}string executable=browser.SelectedIndex==0?"chrome.exe":browser.SelectedIndex==1?"msedge.exe":"firefox.exe";try{foreach(string page in pages)Process.Start(new ProcessStartInfo(executable,"\""+page+"\""){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(dialog,"No se pudo abrir ese navegador: "+ex.Message);}};
    var cookiePath=new TextBox{Left=18,Top=173,Width=449,Height=25,Text=WebMedia.CookieFile??"",ReadOnly=true,BackColor=bg,ForeColor=text};
    var chooseFile=new Button{Text="…",Left=474,Top=171,Width=43,Height=28};chooseFile.Click+=(s,e)=>{using(var picker=new OpenFileDialog{Title="Seleccionar cookies en formato Netscape",Filter="Archivo de cookies (*.txt)|*.txt|Todos los archivos|*.*"})if(picker.ShowDialog(dialog)==DialogResult.OK){cookiePath.Text=picker.FileName;browser.SelectedIndex=3;}};
    var hint=new Label{Text="El archivo se usa solo en este equipo; no se añade al proyecto.",Left=18,Top=205,Width=500,Height=23,ForeColor=Color.FromArgb(170,174,180)};
    var retry=new Button{Text="Reintentar lote",Left=364,Top=242,Width=153,Height=32,DialogResult=DialogResult.OK};
    var cancel=new Button{Text="Dejar pendientes",Left=18,Top=242,Width=136,Height=32,DialogResult=DialogResult.Cancel};
    var copyCommand=new Button{Text="Copiar comando",Left=181,Top=242,Width=146,Height=32};
    copyCommand.Click+=(s,e)=>{if(browser.SelectedIndex<0||browser.SelectedIndex==3&&String.IsNullOrWhiteSpace(cookiePath.Text)){MessageBox.Show(dialog,"Elige primero un navegador o un archivo de cookies.");return;}string option=browser.SelectedIndex==3?"--cookies \""+cookiePath.Text.Replace("\"","\"\"")+"\"":"--cookies-from-browser "+(browser.SelectedIndex==0?"chrome":browser.SelectedIndex==1?"edge":"firefox");Clipboard.SetText(String.Join(Environment.NewLine,pages.Select(page=>"yt-dlp "+option+" \""+page.Replace("\"","%22")+"\"")));copyCommand.Text="Copiado";};
    retry.Click+=(s,e)=>{if(browser.SelectedIndex<0||browser.SelectedIndex==3&&!System.IO.File.Exists(cookiePath.Text)){dialog.DialogResult=DialogResult.None;MessageBox.Show(dialog,browser.SelectedIndex==3?"Selecciona un archivo de cookies válido.":"Elige el navegador donde confirmaste el acceso.");return;}WebMedia.CookieBrowser=browser.SelectedIndex==0?"chrome":browser.SelectedIndex==1?"edge":browser.SelectedIndex==2?"firefox":null;WebMedia.CookieFile=browser.SelectedIndex==3?cookiePath.Text:null;try{SaveWebSettings();}catch(Exception ex){dialog.DialogResult=DialogResult.None;MessageBox.Show(dialog,ex.Message,"No se pudo guardar el acceso");}};
    dialog.Controls.AddRange(new Control[]{heading,domain,note,caption,browser,open,cookiePath,chooseFile,hint,retry,cancel,copyCommand});dialog.AcceptButton=retry;dialog.CancelButton=cancel;
    return dialog.ShowDialog(owner??this)==DialogResult.OK;
   }
  }
 }
}
