using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Prometheus {
 public sealed class MgtControlsPanelView:FlowLayoutPanel {
  static readonly Color Surface=Color.FromArgb(36,38,42),Field=Color.FromArgb(27,28,31),Ink=Color.FromArgb(225,227,230);
  public Action<string,string,object> ParameterChanged;
  public MgtControlsPanelView(){Dock=DockStyle.Fill;AutoScroll=true;FlowDirection=FlowDirection.TopDown;WrapContents=false;BackColor=Surface;ForeColor=Ink;Padding=new Padding(10);Resize+=(s,e)=>ResizeRows();ShowClip(null,null);}
  Label Label(string value,bool heading=false){return new Label{Text=value,ForeColor=Ink,AutoSize=false,Height=heading?28:22,TextAlign=ContentAlignment.MiddleLeft,Font=heading?new Font("Segoe UI",9,FontStyle.Bold):new Font("Segoe UI",9)};}
  void ResizeRows(){foreach(Control control in Controls)control.Width=Math.Max(180,ClientSize.Width-28);}
  static object Value(Dictionary<string,object> map,string key){object result;return map!=null&&map.TryGetValue(key,out result)?result:null;}
  static string TextOf(object value){return value==null?"":Convert.ToString(value,CultureInfo.InvariantCulture);}
  public void ShowClip(Project project,Clip clip){
   SuspendLayout();Controls.Clear();
   var asset=clip==null||project==null?null:project.assets.Find(a=>a.id==clip.asset_id);
   if(!MgtMedia.IsMgt(asset)){Controls.Add(Label("Selecciona una composición MGT en la línea de tiempo."));ResizeRows();ResumeLayout();return;}
   Controls.Add(Label(clip.name,true));Controls.Add(Label("Parámetros de esta instancia · "+asset.container_format));
   var overrides=MgtMedia.Overrides(asset);
   foreach(var item in MgtMedia.Parameters(asset)){
    var group=item as Dictionary<string,object>;if(group==null)continue;
    Controls.Add(Label(TextOf(Value(group,"label")),true));
    var parameterValues=Value(group,"parameters") as System.Collections.IEnumerable;var items=parameterValues==null?null:parameterValues.Cast<object>().ToArray();if(items==null)continue;
    foreach(var entry in items){
     var spec=entry as Dictionary<string,object>;if(spec==null||Value(spec,"editable") is bool&&!(bool)Value(spec,"editable"))continue;
     string key=TextOf(Value(spec,"id")),type=TextOf(Value(spec,"type"));if(String.IsNullOrEmpty(key))continue;
     string title=TextOf(Value(spec,"label"));object current=overrides.ContainsKey(key)?overrides[key]:Value(spec,"default");
     var row=new Panel{Height=54,BackColor=Surface,Margin=new Padding(0,2,0,3)};var name=Label(title);name.Left=0;name.Top=0;name.Width=260;row.Controls.Add(name);
     if(type=="boolean"){
      var check=new CheckBox{Left=0,Top=24,Width=260,Checked=current!=null&&Convert.ToBoolean(current),ForeColor=Ink};
      check.CheckedChanged+=(s,e)=>{if(ParameterChanged!=null)ParameterChanged(clip.id,key,check.Checked);};row.Controls.Add(check);
     }else if(type=="select"){
      var select=new ComboBox{Left=0,Top=24,Width=260,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};
      var choices=Value(spec,"options") as System.Collections.IEnumerable;if(choices!=null)foreach(var option in choices){var choice=option as Dictionary<string,object>;select.Items.Add(choice==null?TextOf(option):TextOf(Value(choice,"value")));}
      int index=select.Items.IndexOf(TextOf(current));if(index>=0)select.SelectedIndex=index;
      select.SelectedIndexChanged+=(s,e)=>{if(select.SelectedItem!=null&&ParameterChanged!=null)ParameterChanged(clip.id,key,TextOf(select.SelectedItem));};row.Controls.Add(select);
     }else{
      var edit=new TextBox{Left=0,Top=24,Width=type=="color"?224:260,Text=TextOf(current),BackColor=Field,ForeColor=Ink,BorderStyle=BorderStyle.FixedSingle};
      edit.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;row.Focus();}};
      edit.Leave+=(s,e)=>{string entered=edit.Text;if(entered==TextOf(current))return;object value=entered;if(type=="number"){double number;if(!double.TryParse(entered,NumberStyles.Float,CultureInfo.CurrentCulture,out number)&&!double.TryParse(entered,NumberStyles.Float,CultureInfo.InvariantCulture,out number)){edit.Text=TextOf(current);return;}value=number;}if(ParameterChanged!=null)ParameterChanged(clip.id,key,value);};row.Controls.Add(edit);
      if(type=="color"){
       var button=new Button{Text="",Left=228,Top=23,Width=31,Height=24,FlatStyle=FlatStyle.Flat,BackColor=Field,ForeColor=Ink};
       try{button.BackColor=ColorTranslator.FromHtml(edit.Text);}catch{}
       button.Click+=(s,e)=>{using(var dialog=new ColorDialog()){try{dialog.Color=ColorTranslator.FromHtml(edit.Text);}catch{}if(dialog.ShowDialog()==DialogResult.OK){string chosen=ColorTranslator.ToHtml(dialog.Color);edit.Text=chosen;button.BackColor=dialog.Color;if(ParameterChanged!=null)ParameterChanged(clip.id,key,chosen);current=chosen;row.Focus();}}};row.Controls.Add(button);
      }
     }
     row.Resize+=(s,e)=>{foreach(Control c in row.Controls.Cast<Control>().Where(c=>c is TextBox||c is ComboBox||c is CheckBox))c.Width=Math.Max(120,row.Width-(type=="color"?40:4));if(type=="color")foreach(Control c in row.Controls.Cast<Control>().Where(c=>c is Button))c.Left=Math.Max(120,row.Width-35);};Controls.Add(row);
    }
   }
   ResizeRows();ResumeLayout();
  }
 }
}
