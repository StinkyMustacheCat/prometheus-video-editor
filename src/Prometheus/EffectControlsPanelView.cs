using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prometheus {
 // One editor for the selected timeline clip or track. Media-origin details live elsewhere.
 public sealed class EffectControlsPanelView:FlowLayoutPanel {
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
  static readonly Color Surface=Color.FromArgb(36,38,42),Field=Color.FromArgb(27,28,31),Ink=Color.FromArgb(225,227,230),Accent=Color.FromArgb(255,156,44);
  static readonly string[] AvailableFonts=FontFamily.Families.Select(f=>f.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase).ToArray();
  readonly ToolTip hints=new ToolTip();
  readonly List<Panel> effectCards=new List<Panel>();
  readonly Dictionary<Panel,Tuple<bool,string,int>> cardOwners=new Dictionary<Panel,Tuple<bool,string,int>>();
  readonly HashSet<Control> catalogDropWiring=new HashSet<Control>();
  readonly HashSet<string> expandedEffects=new HashSet<string>();
  readonly Dictionary<string,Tuple<ScrubNumber,double>> playheadRows=new Dictionary<string,Tuple<ScrubNumber,double>>();
  Project displayedProject;string displayedClipId,displayedTrackId,displayedLinkedAudioId,displayedSignature;
  Panel catalogHoverCard;bool catalogDropAfter;
  bool transformOpen=true,audioOpen=true,maskOpen;
  Button scaleLinkButton;ScrubNumber scaleYControl;bool scaleLinkVisual;
  bool selectedOnTrack;string selectedOwnerId,selectedEffectId;
  public double PlayheadTime;
  public Action<string,string,double> AnimationValueChanged,AnimationValuePreview,AnimationToggleRequested,AnimationResetRequested;public Action<string,string> AnimationClearRequested;public Action<string,string,int> AnimationNavigateRequested;
  public Action<string> TransformParameterSelected;
  public Action<string,string> MaskShapeRequested;
  public Action<string> MaskEditRequested,MaskMonitorEditRequested,MaskRemoveRequested;
  public Action<string,bool> MaskEnabledChanged,MaskInvertedChanged;
  public Action<string,double> MaskFeatherChanged;
   public Action<string,bool> MaskAnimationEnabledChanged;
   public Action<string> MaskKeyframeRequested;
   public Action<string,string> MaskInterpolationChanged;
   public Action<string,bool> Sam2PickRequested,Sam2EnabledChanged;
   public Action<string> Sam2TrackRequested;
   public Action<string> Sam2RemoveRequested;
   public Action<string,int,bool> Sam2LayerEnabledChanged,Sam2LayerVisibleChanged,Sam2LayerInvertedChanged;
   public Action<string,int,double> Sam2LayerFeatherChanged;
   public Action<string,int,string> Sam2LayerBlendChanged;
   public Action Sam2SettingsRequested;
   public Action<string,bool> Sam2CompositeInvertedChanged;
   public Action<string,int> VectorMaskSelected;
   public Action<string,int,bool> VectorMaskVisibleChanged;
   public Action<string,int,string> VectorMaskBlendChanged;
   public Action<string,bool> VectorMaskCompositeInvertedChanged;
   public Action<string,bool,string> MaskTargetChanged;
  public Action<string,string,double,double,double> ClipFramingChanged,ClipFramingPreview;
  public Action<string,double> ScaleYChanged,ScaleYPreview;
  public Action<string,bool> ScaleLinkChanged;
  public Action<string,string> BlendModeChanged;
  public Action<string,double> VolumeChanged,ClipOpacityChanged,ClipOpacityPreview;
  public Action<string,string,int,string> TextAssetChanged;
  public Action<string,string> TextFontChanged;
  public Action<string,string,double> TextStyleNumberChanged;
  public Action<string,string,string> TextStyleColorChanged;
  public Action<bool,string,double> AudioVolumeChanged,AudioBalanceChanged;
  public Action<string> VoiceCleanRequested;
  public Action<string,string> AutoDuckRequested;
  public Action<string> AutoDuckRemoved;
  public Action<string,string,double> AutoDuckParameterChanged;
  public Action<string,bool> ClipEnabledChanged,InheritTrackEffectsChanged,TrackEffectsEnabledChanged,TrackEnabledChanged,ClipFitChanged;
  public Action<bool,string,string,bool> EffectEnabledChanged;
  public Action<bool,string,string> RemoveEffectRequested,ChangeLutRequested;
  public Action<bool,string,string> ResetEffectRequested;
  public Action<bool,string,string,string> ResetParameterRequested;
  public Action<bool,string,string,int> MoveEffectRequested;
  public Action<bool,string,string,int> ReorderEffectRequested;
  public Action<bool,string,string,string,double> NumberChanged;
  public Action<bool,string,string,string,double> NumberPreviewChanged;
  public Action<bool,string,string,string,string> EffectTextChanged;
  public Action<bool,string,string,Action<Color>> ScreenSampleRequested;
  public Action<string> RelinkLutRequested;
  public Action<string,int> TransitionDurationChanged;
  public Action<string> TransitionRemoved;
  public Action<string,bool> TransitionEnabledChanged;
  public Action<string,string,EffectValue> TransitionParameterChanged;
  public Action<bool,string,string,int?> CatalogEffectDropped;
  public EffectControlsPanelView(){Dock=DockStyle.Fill;FlowDirection=FlowDirection.TopDown;WrapContents=false;AutoScroll=true;Padding=new Padding(10);BackColor=Surface;DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AttachCatalogDrop(this);ShowEmpty();}
  int RowWidth {get{return Math.Max(214,ClientSize.Width-34);}}
  void ClearRows(){SuspendLayout();playheadRows.Clear();scaleLinkButton=null;scaleYControl=null;catalogHoverCard=null;effectCards.Clear();cardOwners.Clear();catalogDropWiring.Clear();catalogDropWiring.Add(this);foreach(Control old in Controls.Cast<Control>().ToArray()){Controls.Remove(old);old.Dispose();}}
  void RegisterPlayhead(string target,ScrubNumber row,double multiplier){playheadRows[target]=Tuple.Create(row,multiplier);}
  public void RefreshPlayhead(Project project,double time){PlayheadTime=time;if(project==null||displayedClipId==null||playheadRows.Count==0)return;var clip=project.Active.clips.Find(c=>c.id==displayedClipId);if(clip==null)return;var frame=ClipFraming.Read(clip);foreach(var pair in playheadRows){var row=pair.Value.Item1;if(row.IsDisposed||row.ContainsFocus)continue;string target=pair.Key;double baseline=0;if(target=="transform.scale")baseline=frame.scale;else if(target=="transform.scale_y")baseline=frame.scaleLinked?ClipKeyframes.Value(clip,"transform.scale",time,frame.scale):frame.scaleY;else if(target=="transform.x")baseline=frame.x;else if(target=="transform.y")baseline=frame.y;else if(target=="transform.opacity")baseline=frame.opacity;else if(target.StartsWith("effect:",StringComparison.Ordinal)){int split=target.IndexOf(':',7);if(split<0)continue;var effect=clip.effects.FirstOrDefault(e=>e.id==target.Substring(7,split-7));EffectValue value;if(effect==null||!effect.parameters.TryGetValue(target.Substring(split+1),out value)||!value.number.HasValue)continue;baseline=value.number.Value;}else continue;double shown=frame.scaleLinked&&target=="transform.scale_y"?baseline:ClipKeyframes.Value(clip,target,time,baseline);row.DisplayValue(shown*pair.Value.Item2);row.DisplayKeyframeState(ClipKeyframes.Active(clip,target),ClipKeyframes.At(clip,target,time));}}
  public void UpdateScaleLinkVisual(string clipId,bool linked,double scaleY){if(displayedClipId!=clipId||scaleLinkButton==null||scaleLinkButton.IsDisposed)return;scaleLinkVisual=linked;scaleLinkButton.Text=linked?"🔒":"🔓";scaleLinkButton.ForeColor=linked?Accent:Ink;scaleLinkButton.AccessibleName=linked?"Desenlazar escalas":"Enlazar escalas";hints.SetToolTip(scaleLinkButton,linked?"Escalas enlazadas · clic para ajustar X e Y por separado":"Escalas independientes · clic para conservar proporciones");if(scaleYControl!=null&&!scaleYControl.IsDisposed){scaleYControl.DisplayValue(scaleY*100);scaleYControl.Enabled=!linked;}scaleLinkButton.Invalidate();}
  void FinishRows(){foreach(Control child in Controls)AttachCatalogDrop(child);ResumeLayout(true);}
  Label Label(string value,int height=25){return new Label{Text=value,Width=RowWidth,Height=height,ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};}
  Button SmallButton(string value,Action click,int width=30){var b=new Button{Text=value,Width=width,Height=25,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(50,52,57),ForeColor=Ink,Margin=new Padding(1)};b.FlatAppearance.BorderColor=Color.FromArgb(76,79,85);b.Click+=(s,e)=>{if(click!=null)click();};return b;}
  Button Toggle(string glyph,bool enabled,string description,Action click){var button=SmallButton(glyph=="eye"?"":glyph,click,29);button.Font=new Font("Segoe UI Symbol",9,FontStyle.Bold);button.BackColor=enabled?Accent:Color.FromArgb(50,52,57);button.ForeColor=enabled?Color.FromArgb(25,27,30):Ink;if(glyph=="eye")button.Paint+=(s,e)=>{using(var pen=new Pen(button.ForeColor,1.5f)){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;e.Graphics.DrawEllipse(pen,5,8,18,10);using(var brush=new SolidBrush(button.ForeColor))e.Graphics.FillEllipse(brush,12,10,5,5);}};hints.SetToolTip(button,description);return button;}
  void Header(string title,params Button[] buttons){var bar=new FlowLayoutPanel{Width=RowWidth,Height=33,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Margin=new Padding(0,0,0,8)};int buttonsWidth=buttons.Length*31;bar.Controls.Add(new Label{Text=title,ForeColor=Accent,Font=new Font("Segoe UI",9,FontStyle.Bold),Width=Math.Max(55,RowWidth-buttonsWidth-5),Height=27,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft});foreach(var button in buttons)bar.Controls.Add(button);Controls.Add(bar);}
  void Heading(string value){var l=Label(value,29);l.Font=new Font("Segoe UI",9,FontStyle.Bold);l.ForeColor=Accent;Controls.Add(l);}
  void Note(string value){var l=Label(value,38);l.ForeColor=Color.FromArgb(168,173,180);Controls.Add(l);}
  public void ShowEmpty(){displayedProject=null;displayedSignature=null;displayedClipId=displayedTrackId=displayedLinkedAudioId=null;ClearRows();Heading("CONTROLES DE EFECTOS");Note("Selecciona un clip o una pista de la línea de tiempo.");FinishRows();}
  public void ShowTransition(Project project,TransitionInstance transition){
   ClearRows();displayedSignature=null;displayedProject=project;displayedClipId=displayedTrackId=displayedLinkedAudioId=null;
   var contract=TransitionContracts.Find(transition.transition_id,transition.contract_version);
   var mgt=project.mgt_transitions.FirstOrDefault(r=>r.id==transition.mgt_resource_id);
   Header("TRANSICIÓN · "+(mgt!=null?mgt.name:contract==null?transition.transition_id:contract.name));
   var left=project.Active.clips.Find(c=>c.id==transition.left_clip_id);
   var right=project.Active.clips.Find(c=>c.id==transition.right_clip_id);
   Note((left==null?"?":left.name)+"  →  "+(right==null?"?":right.name));
   var enabled=new CheckBox{Text="Activar transición",Checked=transition.enabled,ForeColor=Ink,Width=RowWidth,Height=27};
   enabled.CheckedChanged+=(s,e)=>{if(TransitionEnabledChanged!=null)TransitionEnabledChanged(transition.id,enabled.Checked);};Controls.Add(enabled);
   int max=left==null||right==null?600:TransitionContracts.MaxLinkedFrames(project,project.Active,left,right,transition.mgt_resource_id??transition.transition_id);
   double minimum=mgt==null?2.0/60:Math.Max(2.0/60,mgt.minimum_seconds),maximum=Math.Max(minimum,max/60.0);
   Number("Duración",transition.duration_frames/60.0,minimum,maximum,minimum,Math.Min(maximum,2),1.0/60,2," s",null,value=>{if(TransitionDurationChanged!=null)TransitionDurationChanged(transition.id,(int)Math.Round(value*60));});
   if(mgt!=null)foreach(var spec in mgt.ParameterDefinitions()){
    string key=Convert.ToString(spec["id"]),kind=Convert.ToString(spec["type"]),label=Convert.ToString(spec.ContainsKey("label")?spec["label"]:key);object initial=spec.ContainsKey("default")?spec["default"]:null;EffectValue saved;
    transition.parameters.TryGetValue(key,out saved);object current=saved==null?initial:kind=="number"?(object)saved.number.GetValueOrDefault():kind=="boolean"?(object)saved.boolean.GetValueOrDefault():saved.text;
    if(kind=="number"){
     double low=spec.ContainsKey("min")?Convert.ToDouble(spec["min"]):-10000,high=spec.ContainsKey("max")?Convert.ToDouble(spec["max"]):10000,value=Convert.ToDouble(current);
     Number(label,value,low,high,Math.Max(low,value-10),Math.Min(high,value+10),spec.ContainsKey("step")?Convert.ToDouble(spec["step"]):0.1,2,"",null,next=>{if(TransitionParameterChanged!=null)TransitionParameterChanged(transition.id,key,new EffectValue{kind="number",number=next});});
    }else if(kind=="boolean"){
     var box=new CheckBox{Text=label,Checked=Convert.ToBoolean(current),ForeColor=Ink,Width=RowWidth,Height=27};box.CheckedChanged+=(s,e)=>{if(TransitionParameterChanged!=null)TransitionParameterChanged(transition.id,key,new EffectValue{kind="boolean",boolean=box.Checked});};Controls.Add(box);
    }else if(kind=="select"){
     var choice=new ComboBox{Width=RowWidth,DropDownStyle=ComboBoxStyle.DropDownList};foreach(var item in spec.ContainsKey("options")?spec["options"] as object[]??new object[0]:new object[0])choice.Items.Add(Convert.ToString(item));choice.SelectedItem=Convert.ToString(current);choice.SelectionChangeCommitted+=(s,e)=>{if(TransitionParameterChanged!=null)TransitionParameterChanged(transition.id,key,new EffectValue{kind="choice",text=Convert.ToString(choice.SelectedItem)});};Controls.Add(Label(label));Controls.Add(choice);
    }else if(kind=="color"){
     var picker=new ColorPickerWidget(label,ColorTranslator.FromHtml(Convert.ToString(current))){Width=RowWidth};picker.ColorChanged=color=>{if(TransitionParameterChanged!=null)TransitionParameterChanged(transition.id,key,new EffectValue{kind="text",text="#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2")});};Controls.Add(picker);
    }else if(kind=="text"||kind=="font"){
     var input=new TextBox{Text=Convert.ToString(current),Width=RowWidth};input.Leave+=(s,e)=>{if(TransitionParameterChanged!=null)TransitionParameterChanged(transition.id,key,new EffectValue{kind="text",text=input.Text});};Controls.Add(Label(label));Controls.Add(input);
    }
   }
   if(contract!=null&&contract.needs_incoming_handle)Note("Usa material anterior del clip entrante. La duración máxima depende de ese margen.");
   var remove=SmallButton("Eliminar transición",()=>{if(TransitionRemoved!=null)TransitionRemoved(transition.id);},RowWidth);remove.Margin=new Padding(0,8,0,0);Controls.Add(remove);
   FinishRows();
  }
  ScrubNumber Number(string label,double value,double min,double max,double sliderMin,double sliderMax,double step,int places,string suffix,Action<double> preview,Action<double> commit){var row=new ScrubNumber(label,value,min,max,sliderMin,sliderMax,step,places,suffix){Width=RowWidth};row.PreviewChanged=preview;row.Committed=commit;Controls.Add(row);return row;}
  ScrubNumber NumberIn(FlowLayoutPanel parent,string label,double value,double min,double max,double sliderMin,double sliderMax,double step,int places,string suffix,Action<double> preview,Action<double> commit){var row=new ScrubNumber(label,value,min,max,sliderMin,sliderMax,step,places,suffix){Width=parent.Width-4};row.PreviewChanged=preview;row.Committed=commit;parent.Controls.Add(row);return row;}
  FlowLayoutPanel Section(string title,bool opened,Action<bool> changed){var body=new FlowLayoutPanel{Width=RowWidth,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=opened,Margin=new Padding(0,0,0,7)};Button button=null;button=SmallButton((opened?"▾  ":"▸  ")+title,()=>{bool next=!body.Visible;body.Visible=next;button.Text=(next?"▾  ":"▸  ")+title;if(changed!=null)changed(next);},RowWidth);button.TextAlign=ContentAlignment.MiddleLeft;button.ForeColor=Accent;button.Font=new Font("Segoe UI",9,FontStyle.Bold);Controls.Add(button);Controls.Add(body);return body;}
  void AddTransform(Clip clip){
   var frame=ClipFraming.Read(clip);var body=Section("TRANSFORMACIÓN",transformOpen,value=>transformOpen=value);
   ScrubNumber scale=null,scaleY=null,x=null,y=null;
   Action<string,double,bool> update=(target,value,preview)=>{
    if(ClipKeyframes.Active(clip,target)){var callback=preview?AnimationValuePreview:AnimationValueChanged;if(callback!=null)callback(clip.id,target,value);return;}
    if(target=="transform.scale_y"){if(preview){if(ScaleYPreview!=null)ScaleYPreview(clip.id,value);}else if(ScaleYChanged!=null)ScaleYChanged(clip.id,value);return;}
    double sc=target=="transform.scale"?value:frame.scale,xx=target=="transform.x"?value:frame.x,yy=target=="transform.y"?value:frame.y;
    if(preview){if(ClipFramingPreview!=null)ClipFramingPreview(clip.id,frame.mode,sc,xx,yy);}else if(ClipFramingChanged!=null)ClipFramingChanged(clip.id,frame.mode,sc,xx,yy);
   };
   scale=NumberIn(body,"Escala X",ClipKeyframes.Value(clip,"transform.scale",PlayheadTime,frame.scale)*100,25,400,25,200,0.5,1," %",v=>update("transform.scale",v/100,true),v=>update("transform.scale",v/100,false));
   var lockRow=new Panel{Width=body.Width-4,Height=22,BackColor=Color.FromArgb(36,38,42),Margin=Padding.Empty};
   var scaleLock=new Button{Text=frame.scaleLinked?"🔒":"🔓",Width=24,Height=22,Left=88,Top=0,FlatStyle=FlatStyle.Flat,BackColor=lockRow.BackColor,ForeColor=frame.scaleLinked?Accent:Ink,Font=new Font("Segoe UI Emoji",10),TabStop=true};
   scaleLock.FlatAppearance.BorderSize=0;scaleLock.AccessibleName=frame.scaleLinked?"Desenlazar escalas":"Enlazar escalas";hints.SetToolTip(scaleLock,frame.scaleLinked?"Escalas enlazadas · clic para ajustar X e Y por separado":"Escalas independientes · clic para conservar proporciones");scaleLinkButton=scaleLock;scaleLinkVisual=frame.scaleLinked;scaleLock.Click+=(s,e)=>{if(ScaleLinkChanged!=null)ScaleLinkChanged(clip.id,!scaleLinkVisual);};lockRow.Controls.Add(scaleLock);body.Controls.Add(lockRow);
   scaleY=NumberIn(body,"Escala Y",(frame.scaleLinked?ClipKeyframes.Value(clip,"transform.scale",PlayheadTime,frame.scale):ClipKeyframes.Value(clip,"transform.scale_y",PlayheadTime,frame.scaleY))*100,25,400,25,200,0.5,1," %",v=>update("transform.scale_y",v/100,true),v=>update("transform.scale_y",v/100,false));scaleYControl=scaleY;scaleY.Enabled=!frame.scaleLinked;
   x=NumberIn(body,"Posición X",ClipKeyframes.Value(clip,"transform.x",PlayheadTime,frame.x)*100,-100,100,-50,50,0.2,1," %",v=>update("transform.x",v/100,true),v=>update("transform.x",v/100,false));
   y=NumberIn(body,"Posición Y",ClipKeyframes.Value(clip,"transform.y",PlayheadTime,frame.y)*100,-100,100,-50,50,0.2,1," %",v=>update("transform.y",v/100,true),v=>update("transform.y",v/100,false));
   scale.Selected=()=>{if(TransformParameterSelected!=null)TransformParameterSelected("transform.scale");};
   scaleY.Selected=()=>{if(TransformParameterSelected!=null)TransformParameterSelected("transform.scale_y");};
   x.Selected=()=>{if(TransformParameterSelected!=null)TransformParameterSelected("transform.x");};
   y.Selected=()=>{if(TransformParameterSelected!=null)TransformParameterSelected("transform.y");};
   scale.EnableReset(()=>{if(AnimationResetRequested!=null)AnimationResetRequested(clip.id,"transform.scale",1);});
   scaleY.EnableReset(()=>{if(AnimationResetRequested!=null)AnimationResetRequested(clip.id,"transform.scale_y",1);});
   x.EnableReset(()=>{if(AnimationResetRequested!=null)AnimationResetRequested(clip.id,"transform.x",0);});
   y.EnableReset(()=>{if(AnimationResetRequested!=null)AnimationResetRequested(clip.id,"transform.y",0);});
   Action<ScrubNumber,string> enable=(row,target)=>row.EnableKeyframe(ClipKeyframes.Active(clip,target),ClipKeyframes.At(clip,target,PlayheadTime),()=>{if(AnimationToggleRequested!=null)AnimationToggleRequested(clip.id,target,row.Value/100);},()=>{if(AnimationClearRequested!=null)AnimationClearRequested(clip.id,target);},direction=>{if(AnimationNavigateRequested!=null)AnimationNavigateRequested(clip.id,target,direction);});
   enable(scale,"transform.scale");enable(scaleY,"transform.scale_y");enable(x,"transform.x");enable(y,"transform.y");RegisterPlayhead("transform.scale",scale,100);RegisterPlayhead("transform.scale_y",scaleY,100);RegisterPlayhead("transform.x",x,100);RegisterPlayhead("transform.y",y,100);
   string opacityTarget="transform.opacity";
   var opacity=NumberIn(body,"Opacidad",ClipKeyframes.Value(clip,opacityTarget,PlayheadTime,frame.opacity)*100,0,100,0,100,0.5,1," %",value=>{if(ClipKeyframes.Active(clip,opacityTarget)){if(AnimationValuePreview!=null)AnimationValuePreview(clip.id,opacityTarget,value/100);}else if(ClipOpacityPreview!=null)ClipOpacityPreview(clip.id,value/100);},value=>{if(ClipKeyframes.Active(clip,opacityTarget)){if(AnimationValueChanged!=null)AnimationValueChanged(clip.id,opacityTarget,value/100);}else if(ClipOpacityChanged!=null)ClipOpacityChanged(clip.id,value/100);});
   opacity.EnableReset(()=>{if(AnimationResetRequested!=null)AnimationResetRequested(clip.id,opacityTarget,1);});enable(opacity,opacityTarget);RegisterPlayhead(opacityTarget,opacity,100);
   var blending=new WheelSafeComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};
   blending.Items.AddRange(new object[]{"Normal","Multiplicar","Trama","Superponer","Oscurecer","Aclarar","Luz suave","Luz fuerte","Diferencia","Exclusión","Añadir","Restar"});blending.SelectedIndex=Array.IndexOf(BlendModes01034.Modes,BlendModes01034.Read(clip));
   blending.SelectedIndexChanged+=(s,e)=>{if(BlendModeChanged!=null&&blending.SelectedIndex>=0)BlendModeChanged(clip.id,BlendModes01034.Modes[blending.SelectedIndex]);};
   body.Controls.Add(new Label{Text="Modo de fusión",Width=body.Width-4,Height=21,ForeColor=Ink});body.Controls.Add(blending);
   if(frame.mode=="fill")body.Controls.Add(new Label{Text="Se conserva el ajuste Rellenar de una versión anterior.",Width=body.Width-4,Height=37,ForeColor=Ink});
  }
  void AddText(Asset asset){
   var body=Section(GeneratedMedia.IsSubtitle(asset)?"SUBTÍTULO":"TEXTO",true,null);
   body.Controls.Add(new Label{Text="Contenido",Width=body.Width-4,Height=22,ForeColor=Ink});
   var content=new TextBox{Text=GeneratedMedia.Text(asset),Multiline=true,Width=body.Width-4,Height=78,MaxLength=1000,BackColor=Field,ForeColor=Ink};body.Controls.Add(content);
   body.Controls.Add(new Label{Text="Fuente",Width=body.Width-4,Height=22,ForeColor=Ink});
   var fontChoice=new ComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};
   string selectedFont=GeneratedMedia.TextFont(asset);var families=AvailableFonts.ToList();
   if(!families.Any(name=>String.Equals(name,selectedFont,StringComparison.OrdinalIgnoreCase)))families.Insert(0,selectedFont);
   fontChoice.Items.AddRange(families.Cast<object>().ToArray());fontChoice.SelectedItem=families.First(name=>String.Equals(name,selectedFont,StringComparison.OrdinalIgnoreCase));
   fontChoice.SelectedIndexChanged+=(s,e)=>{string next=fontChoice.SelectedItem as string;if(next!=null&&next!=GeneratedMedia.TextFont(asset)&&TextFontChanged!=null)TextFontChanged(asset.id,next);};body.Controls.Add(fontChoice);
   string color=GeneratedMedia.TextColor(asset);
   Action<int,string> commitText=(nextSize,nextColor)=>{string next=content.Text.Trim(),hex=nextColor??color;
    if(next.Length>0&&TextAssetChanged!=null&&(next!=GeneratedMedia.Text(asset)||nextSize!=GeneratedMedia.TextSize(asset)||hex!=GeneratedMedia.TextColor(asset)))TextAssetChanged(asset.id,next,nextSize,hex);};
   var size=NumberIn(body,"Tamaño",GeneratedMedia.TextSize(asset),16,1000,16,300,1,0," px",null,value=>commitText((int)Math.Round(value),null));size.HideSlider();size.AcceleratedScrub=true;
   var picker=new ColorPickerWidget("Color",ColorTranslator.FromHtml("#"+color)){Width=body.Width-4};body.Controls.Add(picker);
   content.Leave+=(s,e)=>commitText((int)Math.Round(size.Value),null);
   picker.ColorChanged=next=>{color=next.R.ToString("X2")+next.G.ToString("X2")+next.B.ToString("X2");commitText((int)Math.Round(size.Value),color);};
   NumberIn(body,"Contorno",GeneratedMedia.TextStyleNumber(asset,"stroke_px",4),0,20,0,20,0.25,1," px",null,value=>{if(TextStyleNumberChanged!=null)TextStyleNumberChanged(asset.id,"stroke_px",value);});
   var strokePicker=new ColorPickerWidget("Color del contorno",ColorTranslator.FromHtml("#"+GeneratedMedia.TextStyleColor(asset,"stroke_color","090B10"))){Width=body.Width-4};strokePicker.ColorChanged=next=>{if(TextStyleColorChanged!=null)TextStyleColorChanged(asset.id,"stroke_color",next.R.ToString("X2")+next.G.ToString("X2")+next.B.ToString("X2"));};body.Controls.Add(strokePicker);
   NumberIn(body,"Sombra X",GeneratedMedia.TextStyleNumber(asset,"shadow_x",0),-100,100,-50,50,1,1," px",null,value=>{if(TextStyleNumberChanged!=null)TextStyleNumberChanged(asset.id,"shadow_x",value);});
   NumberIn(body,"Sombra Y",GeneratedMedia.TextStyleNumber(asset,"shadow_y",0),-100,100,-50,50,1,1," px",null,value=>{if(TextStyleNumberChanged!=null)TextStyleNumberChanged(asset.id,"shadow_y",value);});
   NumberIn(body,"Difusión",GeneratedMedia.TextStyleNumber(asset,"shadow_blur",0),0,30,0,30,0.5,1," px",null,value=>{if(TextStyleNumberChanged!=null)TextStyleNumberChanged(asset.id,"shadow_blur",value);});
   NumberIn(body,"Opacidad sombra",GeneratedMedia.TextStyleNumber(asset,"shadow_opacity",0)*100,0,100,0,100,1,1," %",null,value=>{if(TextStyleNumberChanged!=null)TextStyleNumberChanged(asset.id,"shadow_opacity",value/100);});
   var shadowPicker=new ColorPickerWidget("Color de la sombra",ColorTranslator.FromHtml("#"+GeneratedMedia.TextStyleColor(asset,"shadow_color","000000"))){Width=body.Width-4};shadowPicker.ColorChanged=next=>{if(TextStyleColorChanged!=null)TextStyleColorChanged(asset.id,"shadow_color",next.R.ToString("X2")+next.G.ToString("X2")+next.B.ToString("X2"));};body.Controls.Add(shadowPicker);
  }
  void AddAudio(Project project,Clip clip,Track track,bool linked=false){
   var body=Section("AUDIO",audioOpen,value=>audioOpen=value);
   string owner=clip==null?track.id:clip.id;bool onTrack=clip==null;
   if(clip!=null&&linked){var actions=new FlowLayoutPanel{Width=body.Width-4,Height=31,WrapContents=false};actions.Controls.Add(Toggle("♫",clip.enabled,"Silenciar o activar audio vinculado",()=>{if(ClipEnabledChanged!=null)ClipEnabledChanged(clip.id,!clip.enabled);}));actions.Controls.Add(Toggle("Fx",clip.inherit_track_effects,"Usar efectos de la pista de audio",()=>{if(InheritTrackEffectsChanged!=null)InheritTrackEffectsChanged(clip.id,!clip.inherit_track_effects);}));body.Controls.Add(actions);}
   NumberIn(body,"Volumen",onTrack?track.gain_db:clip.gain_db,-60,12,-30,12,0.1,1," dB",null,value=>{if(AudioVolumeChanged!=null)AudioVolumeChanged(onTrack,owner,value);});
   NumberIn(body,"Balance",(onTrack?track.balance:clip.balance)*100,-100,100,-100,100,0.5,1," %",null,value=>{if(AudioBalanceChanged!=null)AudioBalanceChanged(onTrack,owner,value/100);});
   if(clip!=null){var clean=SmallButton(clip.effects.Any(e=>e.effect_id==EffectContracts.VoiceCleanId)?"Limpieza de voz aplicada":"Limpiar voz en un clic",()=>{if(VoiceCleanRequested!=null)VoiceCleanRequested(clip.id);},Math.Min(body.Width-4,190));hints.SetToolTip(clean,"Reduce ruido y clics, comprime y limita la voz. Ajusta la intensidad en el efecto.");body.Controls.Add(clean);}
   if(onTrack){
    var voices=project.Active.tracks.Where(t=>t.kind=="audio"&&t.id!=track.id).OrderBy(t=>t.order).ToList();var rule=Ducking01016.For(project.Active,track.id);
    if(voices.Count>0){var source=new ComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};foreach(var voice in voices)source.Items.Add(voice.name);int current=rule==null?0:voices.FindIndex(t=>t.id==rule.voice_track_id);source.SelectedIndex=current<0?0:current;body.Controls.Add(new Label{Text="Voz que atenúa esta pista",Width=body.Width-4,Height=22,ForeColor=Ink});body.Controls.Add(source);
     body.Controls.Add(SmallButton(rule==null?"Activar Auto-Ducking":"Actualizar pista de voz",()=>{if(AutoDuckRequested!=null)AutoDuckRequested(track.id,voices[source.SelectedIndex].id);},Math.Min(body.Width-4,190)));
    }
    if(rule!=null){
     NumberIn(body,"Reducción aproximada",-rule.amount_db,1,30,1,24,0.5,1," dB",null,value=>{if(AutoDuckParameterChanged!=null)AutoDuckParameterChanged(track.id,"amount_db",-value);});
     NumberIn(body,"Sensibilidad",rule.threshold_db,-60,-6,-50,-10,0.5,1," dB",null,value=>{if(AutoDuckParameterChanged!=null)AutoDuckParameterChanged(track.id,"threshold_db",value);});
     NumberIn(body,"Entrada",rule.attack_ms,5,1000,5,300,5,1," ms",null,value=>{if(AutoDuckParameterChanged!=null)AutoDuckParameterChanged(track.id,"attack_ms",value);});
     NumberIn(body,"Salida",rule.release_ms,50,3000,50,1000,10,1," ms",null,value=>{if(AutoDuckParameterChanged!=null)AutoDuckParameterChanged(track.id,"release_ms",value);});
     body.Controls.Add(SmallButton("Quitar Auto-Ducking",()=>{if(AutoDuckRemoved!=null)AutoDuckRemoved(track.id);},Math.Min(body.Width-4,190)));
    }
   }
   var effects=onTrack?track.effects:clip.effects;
   var title=new Label{Text=onTrack?"EFECTOS DE PISTA":"EFECTOS DE AUDIO",Width=body.Width-4,Height=27,ForeColor=Accent,Font=new Font("Segoe UI",9,FontStyle.Bold)};body.Controls.Add(title);
   if(effects.Count==0)body.Controls.Add(new Label{Text="Arrastra un efecto de audio desde Efectos.",Width=body.Width-4,Height=36,ForeColor=Ink});
   for(int i=0;i<effects.Count;i++)AddEffect(project,onTrack,owner,effects[i],i,effects.Count,body);
  }
   void AddSam2Layers0104(FlowLayoutPanel body,Clip clip,Sam2MaskTrack090 track){
    body.Controls.Add(new Label{Text="MÁSCARAS DE OBJETO",Width=body.Width-4,Height=22,ForeColor=Accent,Font=new Font("Segoe UI",9,FontStyle.Bold)});
    foreach(var layer in Sam2MaskStore090.Layers(track)){
     int objectId=layer.object_id;
     var row=new FlowLayoutPanel{Width=body.Width-4,Height=30,WrapContents=false,BackColor=Color.FromArgb(42,45,50)};
     var active=new CheckBox{Text="Objeto "+objectId,Checked=layer.enabled,Width=110,Height=27,ForeColor=Ink};
     active.CheckedChanged+=(s,e)=>{if(Sam2LayerEnabledChanged!=null)Sam2LayerEnabledChanged(clip.id,objectId,active.Checked);};row.Controls.Add(active);
     var eye=SmallButton(layer.overlay_visible?"◉":"◎",()=>{if(Sam2LayerVisibleChanged!=null)Sam2LayerVisibleChanged(clip.id,objectId,!layer.overlay_visible);},32);eye.AccessibleName="Mostrar contorno de esta máscara en el monitor";row.Controls.Add(eye);
     var inverse=new CheckBox{Text="Invertir",Checked=layer.inverted,Width=80,Height=27,ForeColor=Ink};inverse.CheckedChanged+=(s,e)=>{if(Sam2LayerInvertedChanged!=null)Sam2LayerInvertedChanged(clip.id,objectId,inverse.Checked);};row.Controls.Add(inverse);body.Controls.Add(row);
     var blend=new ComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};blend.Items.AddRange(new object[]{"Añadir", "Restar", "Intersecar"});blend.SelectedIndex=layer.blend=="subtract"?1:layer.blend=="intersect"?2:0;blend.SelectedIndexChanged+=(s,e)=>{if(Sam2LayerBlendChanged!=null)Sam2LayerBlendChanged(clip.id,objectId,blend.SelectedIndex==1?"subtract":blend.SelectedIndex==2?"intersect":"add");};body.Controls.Add(blend);
     var feather=NumberIn(body,"Suavidad · objeto "+objectId,layer.feather*100,0,20,0,10,0.1,1," %",null,value=>{if(Sam2LayerFeatherChanged!=null)Sam2LayerFeatherChanged(clip.id,objectId,value/100);});
     feather.EnableReset(()=>{if(Sam2LayerFeatherChanged!=null)Sam2LayerFeatherChanged(clip.id,objectId,0);});
    }
    var global=new CheckBox{Text="Invertir resultado de todas las máscaras",Checked=track.composite_inverted,Width=body.Width-4,Height=28,ForeColor=Ink};global.CheckedChanged+=(s,e)=>{if(Sam2CompositeInvertedChanged!=null)Sam2CompositeInvertedChanged(clip.id,global.Checked);};body.Controls.Add(global);
   }
   void AddMask(Project project,Clip clip){
   var body=Section("MÁSCARA",maskOpen,value=>maskOpen=value);
    var sam2=Sam2MaskStore090.Get(clip);var maskAsset=project.assets.Find(a=>a.id==clip.asset_id);var maskStream=maskAsset==null?null:maskAsset.streams.Find(st=>st.id==clip.stream_id);bool stillImage=maskStream!=null&&maskStream.kind=="image";
    if(sam2!=null){body.Controls.Add(new Label{Text=sam2.stale?"Máscara inteligente · Recalcular tras cambio MGT":stillImage?"Máscara inteligente · Imagen fija":sam2.static_preview?"Máscara inteligente · Sin seguimiento":"Máscara inteligente · Seguimiento listo",Width=body.Width-4,Height=23,ForeColor=Color.FromArgb(255,190,120)});if(sam2.static_preview&&!stillImage&&!sam2.stale)body.Controls.Add(SmallButton("▶  Seguir objeto en el clip",()=>{if(Sam2TrackRequested!=null)Sam2TrackRequested(clip.id);},Math.Min(body.Width-4,220)));body.Controls.Add(SmallButton("Quitar recorte inteligente",()=>{if(Sam2RemoveRequested!=null)Sam2RemoveRequested(clip.id);},Math.Min(body.Width-4,215)));var sam2Enabled=new CheckBox{Text="Aplicar recorte inteligente",Checked=sam2.enabled,Width=body.Width-4,Height=27,ForeColor=Ink};sam2Enabled.CheckedChanged+=(s,e)=>{if(Sam2EnabledChanged!=null)Sam2EnabledChanged(clip.id,sam2Enabled.Checked);};body.Controls.Add(sam2Enabled);if(!Sam2MaskStore090.Ready(clip))body.Controls.Add(new Label{Text="Datos del recorte ausentes o desactualizados · usa la herramienta para recalcular",Width=body.Width-4,Height=35,ForeColor=Color.FromArgb(255,190,120)});AddSam2Layers0104(body,clip,sam2);}
    if(clip.mask!=null||sam2!=null){var choices=new List<string>{"unassigned","opacity"};var labels=new List<string>{"Sin aplicar","Opacidad"};foreach(var effect in EffectContracts.Effective(project,clip)){var contract=EffectContracts.Find(effect.effect_id,effect.contract_version);if(contract==null||contract.input_kind!="video"||effect.effect_id==EffectContracts.ZoomInDynamicId||effect.effect_id==EffectContracts.ZoomOutDynamicId||effect.effect_id==EffectContracts.PanDynamicId)continue;choices.Add("effect:"+effect.id);labels.Add("Efecto · "+contract.name);}var destination=new ComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};destination.Items.AddRange(labels.Cast<object>().ToArray());int selected=choices.IndexOf(TemporalMasks0823.Target(clip));destination.SelectedIndex=Math.Max(0,selected);destination.SelectedIndexChanged+=(s,e)=>{int index=destination.SelectedIndex;if(destination.IsDisposed||index<0||index>=choices.Count)return;if(MaskTargetChanged!=null)MaskTargetChanged(clip.id,sam2!=null,choices[index]);};body.Controls.Add(new Label{Text="Aplicar máscara a",Width=body.Width-4,Height=21,ForeColor=Ink});body.Controls.Add(destination);}
   var vectorLayers=VectorMaskStack0104.All(clip);if(vectorLayers.Count==0){body.Controls.Add(new Label{Text="Crea una máscara desde la herramienta 👤▾ de la línea de tiempo. Esc cancela; Intro aplica la pluma.",Width=body.Width-4,Height=48,ForeColor=Ink});return;}
   int selectedVector=VectorMaskStack0104.SelectedId(clip);
   foreach(var layer in vectorLayers){int layerId=layer.id;var row=new FlowLayoutPanel{Width=body.Width-4,Height=30,WrapContents=false,Margin=Padding.Empty};var choose=SmallButton((layer.id==selectedVector?"◆ ":"◇ ")+(layer.mask.shape=="rectangle"?"Rectángulo":layer.mask.shape=="ellipse"?"Elipse":"Pluma")+" "+layer.id,()=>{if(VectorMaskSelected!=null)VectorMaskSelected(clip.id,layerId);},Math.Max(110,body.Width-95));row.Controls.Add(choose);var eye=SmallButton(layer.overlay_visible?"◉":"◎",()=>{if(VectorMaskVisibleChanged!=null)VectorMaskVisibleChanged(clip.id,layerId,!layer.overlay_visible);},32);eye.AccessibleName="Mostrar máscara en el monitor";row.Controls.Add(eye);body.Controls.Add(row);}
   var chosenLayer=vectorLayers.First(x=>x.id==selectedVector);var mask=chosenLayer.mask;
   var invertAll=new CheckBox{Text="Invertir resultado de todas las máscaras",Checked=VectorMaskStack0104.Get(clip).composite_inverted,Width=body.Width-4,Height=28,ForeColor=Ink};invertAll.CheckedChanged+=(s,e)=>{if(VectorMaskCompositeInvertedChanged!=null)VectorMaskCompositeInvertedChanged(clip.id,invertAll.Checked);};body.Controls.Add(invertAll);
   if(vectorLayers.Count>1){var blend=new ComboBox{Width=body.Width-4,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};blend.Items.AddRange(new object[]{"Añadir","Restar","Intersecar"});blend.SelectedIndex=chosenLayer.blend=="subtract"?1:chosenLayer.blend=="intersect"?2:0;blend.SelectedIndexChanged+=(s,e)=>{if(VectorMaskBlendChanged!=null)VectorMaskBlendChanged(clip.id,selectedVector,blend.SelectedIndex==1?"subtract":blend.SelectedIndex==2?"intersect":"add");};body.Controls.Add(blend);}
   var active=new CheckBox{Text="Activar máscara",Checked=mask.enabled,Width=body.Width-4,Height=28,ForeColor=Ink};active.CheckedChanged+=(s,e)=>{if(MaskEnabledChanged!=null)MaskEnabledChanged(clip.id,active.Checked);};body.Controls.Add(active);
   var inverted=new CheckBox{Text="Invertir área",Checked=mask.inverted,Width=body.Width-4,Height=28,ForeColor=Ink};inverted.CheckedChanged+=(s,e)=>{if(MaskInvertedChanged!=null)MaskInvertedChanged(clip.id,inverted.Checked);};body.Controls.Add(inverted);
   var maskFeather=NumberIn(body,"Suavidad",mask.feather*100,0,20,0,10,0.1,1," %",null,value=>{if(MaskFeatherChanged!=null)MaskFeatherChanged(clip.id,value/100);});
   maskFeather.EnableReset(()=>{if(MaskFeatherChanged!=null)MaskFeatherChanged(clip.id,0);});
    var motion=selectedVector==1?TemporalMasks0823.Get(clip):null;
    if(selectedVector==1){var animate=new CheckBox{Text="Animar máscara",Checked=motion!=null&&motion.enabled,Width=body.Width-4,Height=27,ForeColor=Ink};animate.CheckedChanged+=(s,e)=>{if(MaskAnimationEnabledChanged!=null)MaskAnimationEnabledChanged(clip.id,animate.Checked);};body.Controls.Add(animate);}
    if(motion!=null&&motion.enabled){var row=new FlowLayoutPanel{Width=body.Width-4,Height=31,WrapContents=false};row.Controls.Add(SmallButton("◆  Fotograma",()=>{if(MaskKeyframeRequested!=null)MaskKeyframeRequested(clip.id);},Math.Min(125,body.Width/2)));var curve=new ComboBox{Width=Math.Max(95,Math.Min(135,body.Width/2-8)),DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Field,ForeColor=Ink};curve.Items.AddRange(new object[]{"Lineal","Bézier","Mantener"});curve.SelectedIndex=motion.interpolation=="bezier"?1:motion.interpolation=="hold"?2:0;curve.SelectedIndexChanged+=(s,e)=>{if(MaskInterpolationChanged!=null)MaskInterpolationChanged(clip.id,curve.SelectedIndex==1?"bezier":curve.SelectedIndex==2?"hold":"linear");};row.Controls.Add(curve);body.Controls.Add(row);}
   body.Controls.Add(SmallButton("Editar en monitor",()=>{if(MaskMonitorEditRequested!=null)MaskMonitorEditRequested(clip.id);},Math.Min(body.Width-4,180)));
   body.Controls.Add(SmallButton("Eliminar máscara",()=>{if(MaskRemoveRequested!=null)MaskRemoveRequested(clip.id);},Math.Min(body.Width-4,155)));
  }
  public void ShowClip(Project project,Clip clip){
   if(clip==null){if(displayedClipId==null&&displayedTrackId==null&&displayedProject==null)return;ShowEmpty();return;}if(displayedClipId==clip.id&&Object.ReferenceEquals(displayedProject,project)){RefreshPlayhead(project,PlayheadTime);return;}var currentTrack=project.Active.tracks.First(t=>t.id==clip.track_id);var currentAsset=project.assets.Find(a=>a.id==clip.asset_id);var currentLink=project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(clip.id));var currentAudio=currentLink==null?null:project.Active.clips.FirstOrDefault(c=>currentLink.clip_ids.Contains(c.id)&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio"));string signature=Store.Json().Serialize(new{clip.name,clip.asset_id,clip.enabled,clip.inherit_track_effects,clip.gain_db,clip.animation,clip.effects,clip.extensions,track=currentTrack,asset_extensions=currentAsset==null?null:currentAsset.extensions,audio=currentAudio==null?null:Store.Fields(currentAudio)});if(displayedClipId==clip.id&&displayedSignature==signature){displayedProject=project;RefreshPlayhead(project,PlayheadTime);return;}int oldScroll=-AutoScrollPosition.Y;bool held=IsHandleCreated;if(held)SendMessage(Handle,0x0B,IntPtr.Zero,IntPtr.Zero);try{ClearRows();
   displayedSignature=signature;
   displayedProject=project;displayedClipId=clip.id;displayedTrackId=null;displayedLinkedAudioId=null;
   var track=project.Active.tracks.First(t=>t.id==clip.track_id);var framing=ClipFraming.Read(clip);
   var headerButtons=new System.Collections.Generic.List<Button>();
   headerButtons.Add(Toggle(track.kind=="audio"?"♫":"eye",clip.enabled,track.kind=="audio"?"Silenciar o activar este clip":"Ocultar o mostrar este clip",()=>{if(ClipEnabledChanged!=null)ClipEnabledChanged(clip.id,!clip.enabled);}));
   headerButtons.Add(Toggle("Fx",clip.inherit_track_effects,"Usar efectos de pista en este clip"+(track.effects_enabled?"":" · Fx de pista apagado"),()=>{if(InheritTrackEffectsChanged!=null)InheritTrackEffectsChanged(clip.id,!clip.inherit_track_effects);}));
   if(track.kind=="video"){
    headerButtons.Add(Toggle("▣",framing.mode!="native","Ajustar al lienzo · desactivar para tamaño original",()=>{if(ClipFitChanged!=null)ClipFitChanged(clip.id,framing.mode=="native");}));
   }
   Header("CLIP · "+clip.name,headerButtons.ToArray());
   if(track.kind=="audio"){
    AddAudio(project,clip,track);
   }else{
    var asset=project.assets.Find(a=>a.id==clip.asset_id);if(GeneratedMedia.IsText(asset))AddText(asset);
    AddTransform(clip);AddMask(project,clip);
    var link=project.Active.link_groups.FirstOrDefault(g=>g.clip_ids.Contains(clip.id));
    var linkedAudio=link==null?null:project.Active.clips.FirstOrDefault(c=>link.clip_ids.Contains(c.id)&&project.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="audio"));
    if(linkedAudio!=null){displayedLinkedAudioId=linkedAudio.id;AddAudio(project,linkedAudio,project.Active.tracks.First(t=>t.id==linkedAudio.track_id),true);}
    if(track.effects.Count>0&&!track.effects_enabled)Note("Fx de pista desactivado: no afecta al monitor ni a la exportación.");
    Heading("EFECTOS DEL CLIP");
    if(clip.effects.Count==0)Note("Arrastra un efecto desde el panel Efectos.");
    for(int i=0;i<clip.effects.Count;i++)AddEffect(project,false,clip.id,clip.effects[i],i,clip.effects.Count);
   }
    FinishRows();AutoScrollPosition=new Point(0,oldScroll);
   }finally{ResumeLayout(true);if(held&&!IsDisposed){SendMessage(Handle,0x0B,new IntPtr(1),IntPtr.Zero);Invalidate(true);}}
  }
  public void ShowTrack(Project project,Track track){
   if(track==null){ShowEmpty();return;}if(displayedTrackId==track.id&&Object.ReferenceEquals(displayedProject,project))return;string signature=Store.Json().Serialize(Store.Fields(track));if(displayedTrackId==track.id&&displayedSignature==signature){displayedProject=project;return;}displayedSignature=signature;ClearRows();Header("PISTA · "+track.name,Toggle(track.kind=="audio"?"♫":"eye",track.enabled,track.kind=="audio"?"Silenciar o activar pista":"Ocultar o mostrar pista",()=>{if(TrackEnabledChanged!=null)TrackEnabledChanged(track.id,!track.enabled);}),Toggle("Fx",track.effects_enabled,"Activar o desactivar efectos de pista",()=>{if(TrackEffectsEnabledChanged!=null)TrackEffectsEnabledChanged(track.id,!track.effects_enabled);}));
   displayedProject=project;displayedClipId=null;displayedTrackId=track.id;displayedLinkedAudioId=null;
   if(track.kind!="video"){AddAudio(project,null,track);FinishRows();return;}
   Note("Se aplica a cada clip de esta pista antes de componerlo.");
   Heading("EFECTOS DE PISTA");if(track.effects.Count==0)Note("Arrastra un efecto hasta la cabecera de la pista.");
   for(int i=0;i<track.effects.Count;i++)AddEffect(project,true,track.id,track.effects[i],i,track.effects.Count);
   FinishRows();
  }
  void SelectEffectCard(bool onTrack,string ownerId,string effectId,Panel card){
   selectedOnTrack=onTrack;selectedOwnerId=ownerId;selectedEffectId=effectId;
   foreach(var row in effectCards)row.BackColor=row==card?Color.FromArgb(78,62,44):Color.FromArgb(44,46,51);
   card.Focus();
  }
  public bool DeleteSelectedEffect(){
   if(!ContainsFocus||selectedEffectId==null||!effectCards.Any(c=>Equals(c.Tag,selectedEffectId)))return false;
   if(RemoveEffectRequested!=null)RemoveEffectRequested(selectedOnTrack,selectedOwnerId,selectedEffectId);
   return true;
  }
  void AddEffect(Project project,bool onTrack,string ownerId,EffectInstance effect,int index,int count,FlowLayoutPanel parent=null){
   var contract=EffectContracts.Find(effect.effect_id,effect.contract_version);
   bool expandable=contract==null||contract.parameters.Length>0,opened=expandedEffects.Contains(effect.id);
   int width=parent==null?RowWidth:parent.Width-4;
   var card=new Panel{Width=width,Height=33,BackColor=selectedEffectId==effect.id&&selectedOwnerId==ownerId?Color.FromArgb(78,62,44):Color.FromArgb(44,46,51),Margin=new Padding(0,4,0,4),TabStop=true,Tag=effect.id,AllowDrop=true};
   bool dropActive=false,dropAfter=false;card.Paint+=(s,e)=>{if(!dropActive&&catalogHoverCard!=card)return;bool after=catalogHoverCard==card?catalogDropAfter:dropAfter;using(var pen=new Pen(Accent,3))e.Graphics.DrawLine(pen,1,after?card.Height-2:1,card.Width-2,after?card.Height-2:1);};
   effectCards.Add(card);
   cardOwners[card]=Tuple.Create(onTrack,ownerId,index);
   var menu=new ContextMenuStrip();menu.Items.Add("Eliminar efecto",null,(s,e)=>{if(RemoveEffectRequested!=null)BeginInvoke((Action)(()=>RemoveEffectRequested(onTrack,ownerId,effect.id)));});
   if(expandable&&contract!=null)menu.Items.Add("Restablecer efecto completo",null,(s,e)=>{if(ResetEffectRequested!=null)BeginInvoke((Action)(()=>ResetEffectRequested(onTrack,ownerId,effect.id)));});
   card.ContextMenuStrip=menu;card.Disposed+=(s,e)=>menu.Dispose();
   var head=new FlowLayoutPanel{Dock=DockStyle.Top,Height=30,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,ContextMenuStrip=menu};
   Button expand=null;if(expandable){expand=SmallButton(opened?"▾":"▸",null,24);head.Controls.Add(expand);}
   var active=new CheckBox{Checked=effect.enabled,Width=19,Height=24,Margin=new Padding(2,3,0,0),ContextMenuStrip=menu};
   active.CheckedChanged+=(s,e)=>{if(EffectEnabledChanged!=null)EffectEnabledChanged(onTrack,ownerId,effect.id,active.Checked);};head.Controls.Add(active);
   var name=new Label{Text=contract==null?"No disponible · "+effect.effect_id:contract.name,Width=Math.Max(54,width-(expandable?109:87)),Height=26,ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,ContextMenuStrip=menu};head.Controls.Add(name);
   var up=SmallButton("↑",()=>{if(MoveEffectRequested!=null)MoveEffectRequested(onTrack,ownerId,effect.id,-1);},22);up.Enabled=index>0;head.Controls.Add(up);
   var down=SmallButton("↓",()=>{if(MoveEffectRequested!=null)MoveEffectRequested(onTrack,ownerId,effect.id,1);},22);down.Enabled=index<count-1;head.Controls.Add(down);
   var body=new FlowLayoutPanel{Left=8,Top=32,Width=width-16,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Visible=opened};
   if(contract==null)body.Controls.Add(new Label{Text="Conservado en el proyecto; esta versión no puede procesarlo.",Width=body.Width,Height=42,ForeColor=Ink});
   else foreach(var spec in contract.parameters){
    if(spec.kind=="number"){
     var value=effect.parameters[spec.id];string parameter=spec.id;var ownerClip=onTrack?null:project.Active.clips.Find(c=>c.id==ownerId);string animationTarget="effect:"+effect.id+":"+parameter;
     double shown=ownerClip!=null&&spec.animatable?ClipKeyframes.Value(ownerClip,animationTarget,PlayheadTime,value.number.Value):value.number.Value;
     double low=spec.minimum.Value,high=spec.maximum.Value,shownHigh=Math.Min(high,Math.Max(low+1,shown*2+1));
     var row=new ScrubNumber(ParameterLabel(spec.id),shown,low,high,low,shownHigh,(high-low)/400,2,spec.id=="left"||spec.id=="top"||spec.id=="right"||spec.id=="bottom"?" %":spec.id=="frequency"?" Hz":spec.id=="zoom_percent"||spec.id=="distance_percent"?" %":""){Width=body.Width-6};
     if(contract.input_kind=="video")row.PreviewChanged=next=>{if(NumberPreviewChanged!=null)NumberPreviewChanged(onTrack,ownerId,effect.id,parameter,next);};
     row.Committed=next=>{if(NumberChanged!=null)NumberChanged(onTrack,ownerId,effect.id,parameter,next);};
     if(spec.initial!=null)row.EnableReset(()=>{if(ResetParameterRequested!=null)ResetParameterRequested(onTrack,ownerId,effect.id,parameter);});
     if(ownerClip!=null&&spec.animatable){row.EnableKeyframe(ClipKeyframes.Active(ownerClip,animationTarget),ClipKeyframes.At(ownerClip,animationTarget,PlayheadTime),()=>{if(AnimationToggleRequested!=null)AnimationToggleRequested(ownerClip.id,animationTarget,row.Value);},()=>{if(AnimationClearRequested!=null)AnimationClearRequested(ownerClip.id,animationTarget);},direction=>{if(AnimationNavigateRequested!=null)AnimationNavigateRequested(ownerClip.id,animationTarget,direction);});RegisterPlayhead(animationTarget,row,1);}
     body.Controls.Add(row);
    }else if(spec.kind=="resource"){
     var resource=project.effect_resources.FirstOrDefault(r=>r.id==effect.parameters[spec.id].resource_id);
     body.Controls.Add(new Label{Text=resource==null?"LUT no disponible":Path.GetFileName(resource.uri),Width=body.Width-6,Height=24,ForeColor=Ink,AutoEllipsis=true});
     if(resource==null||!File.Exists(resource.uri))body.Controls.Add(SmallButton("Localizar LUT…",()=>{if(resource!=null&&RelinkLutRequested!=null)RelinkLutRequested(resource.id);},132));
     else body.Controls.Add(SmallButton("Cambiar LUT…",()=>{if(ChangeLutRequested!=null)ChangeLutRequested(onTrack,ownerId,effect.id);},132));
    }else if(spec.kind=="choice"){
     var choice=new Panel{Width=body.Width-6,Height=27,BackColor=Surface};
     var label=new Label{Text=ParameterLabel(spec.id)+": "+effect.parameters[spec.id].text,Width=choice.Width-31,Height=25,ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};choice.Controls.Add(label);
     if(spec.initial!=null){string parameter=spec.id;var reset=SmallButton("↶",()=>{if(ResetParameterRequested!=null)ResetParameterRequested(onTrack,ownerId,effect.id,parameter);},23);reset.Left=choice.Width-26;reset.Top=1;hints.SetToolTip(reset,"Restablecer "+ParameterLabel(parameter));choice.Controls.Add(reset);}
     body.Controls.Add(choice);
    }
    else if(spec.kind=="text"&&spec.id=="color"){
     var picker=new ColorPickerWidget("Color clave",ColorTranslator.FromHtml(effect.parameters["color"].text)){Width=body.Width-6};
     picker.SampleRequested=apply=>{if(ScreenSampleRequested!=null)ScreenSampleRequested(onTrack,ownerId,effect.id,apply);};
     picker.ColorChanged=color=>{if(EffectTextChanged!=null)EffectTextChanged(onTrack,ownerId,effect.id,"color","#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2"));};
     if(spec.initial!=null)picker.EnableReset(()=>{if(ResetParameterRequested!=null)ResetParameterRequested(onTrack,ownerId,effect.id,"color");});
     body.Controls.Add(picker);
    }
   }
   Action updateCardHeight=()=>{
    if(card.IsDisposed)return;
    int contentHeight=body.Controls.Cast<Control>().Sum(control=>control.Height+control.Margin.Vertical)+body.Padding.Vertical;
    int desired=body.Visible?39+Math.Max(28,contentHeight):33;
    if(card.Height!=desired)card.Height=desired;
   };
   foreach(Control control in body.Controls)control.SizeChanged+=(s,e)=>updateCardHeight();
   body.SizeChanged+=(s,e)=>updateCardHeight();
   updateCardHeight();
   if(expand!=null)expand.Click+=(s,e)=>{bool next=!body.Visible;body.Visible=next;expand.Text=next?"▾":"▸";if(next)expandedEffects.Add(effect.id);else expandedEffects.Remove(effect.id);updateCardHeight();};
   Point dragStart=Point.Empty;
   name.MouseDown+=(s,e)=>{SelectEffectCard(onTrack,ownerId,effect.id,card);if(e.Button==MouseButtons.Left)dragStart=Cursor.Position;};
   name.MouseMove+=(s,e)=>{if(e.Button!=MouseButtons.Left||dragStart==Point.Empty)return;var current=Cursor.Position;if(Math.Abs(current.X-dragStart.X)+Math.Abs(current.Y-dragStart.Y)<6)return;dragStart=Point.Empty;var data=new DataObject();data.SetData("Prometheus.EffectRow",(onTrack?"1":"0")+"|"+ownerId+"|"+effect.id);card.DoDragDrop(data,DragDropEffects.Move);};
   head.MouseDown+=(s,e)=>SelectEffectCard(onTrack,ownerId,effect.id,card);
   card.MouseDown+=(s,e)=>SelectEffectCard(onTrack,ownerId,effect.id,card);
   Action<Control> attachDrop=null;attachDrop=target=>{
    target.AllowDrop=true;
    target.DragEnter+=(s,e)=>{string data=e.Data.GetData("Prometheus.EffectRow") as string;e.Effect=data!=null&&data.StartsWith((onTrack?"1":"0")+"|"+ownerId+"|",StringComparison.Ordinal)?DragDropEffects.Move:DragDropEffects.None;};
    target.DragOver+=(s,e)=>{string data=e.Data.GetData("Prometheus.EffectRow") as string;e.Effect=data!=null&&data.StartsWith((onTrack?"1":"0")+"|"+ownerId+"|",StringComparison.Ordinal)?DragDropEffects.Move:DragDropEffects.None;dropActive=e.Effect!=DragDropEffects.None;dropAfter=card.PointToClient(new Point(e.X,e.Y)).Y>card.Height/2;card.Invalidate();};
    target.DragLeave+=(s,e)=>{dropActive=false;card.Invalidate();};
    target.DragDrop+=(s,e)=>{dropActive=false;card.Invalidate();string data=e.Data.GetData("Prometheus.EffectRow") as string;if(data==null)return;var bits=data.Split('|');if(bits.Length!=3||bits[0]!=(onTrack?"1":"0")||bits[1]!=ownerId)return;bool after=card.PointToClient(new Point(e.X,e.Y)).Y>card.Height/2;if(ReorderEffectRequested!=null)ReorderEffectRequested(onTrack,ownerId,bits[2],index+(after?1:0));};
    foreach(Control child in target.Controls)attachDrop(child);
   };
   card.Controls.Add(body);card.Controls.Add(head);attachDrop(card);
   if(parent==null)Controls.Add(card);else parent.Controls.Add(card);
  }
  void SetCatalogHover(Panel card,bool after){
   if(catalogHoverCard==card&&catalogDropAfter==after)return;
   var previous=catalogHoverCard;catalogHoverCard=card;catalogDropAfter=after;
   if(previous!=null&&!previous.IsDisposed)previous.Invalidate();
   if(card!=null&&!card.IsDisposed)card.Invalidate();
  }
  bool CatalogTarget(Control hovered,string effectId,Point screen,out bool onTrack,out string ownerId,out int? slot,out Panel card){
   onTrack=false;ownerId=null;slot=null;card=null;
   if(displayedProject==null)return false;
   var contract=EffectContracts.All.FirstOrDefault(item=>item.id==effectId);
   if(contract==null)return false;
   for(Control current=hovered;current!=null&&current!=this;current=current.Parent){
    var candidate=current as Panel;
    if(candidate!=null&&cardOwners.ContainsKey(candidate)){card=candidate;break;}
   }
   if(card!=null){
    var target=cardOwners[card];onTrack=target.Item1;ownerId=target.Item2;
    slot=target.Item3+(card.PointToClient(screen).Y>card.Height/2?1:0);
   }else if(displayedTrackId!=null){onTrack=true;ownerId=displayedTrackId;}
   else if(displayedClipId!=null){
    var shown=displayedProject.Active.clips.Find(c=>c.id==displayedClipId);
    var shownTrack=shown==null?null:displayedProject.Active.tracks.Find(t=>t.id==shown.track_id);
    ownerId=shownTrack!=null&&shownTrack.kind=="video"&&contract.input_kind=="audio"?displayedLinkedAudioId:displayedClipId;
   }
   if(ownerId==null)return false;
   string targetId=ownerId;
   var ownerClip=onTrack?null:displayedProject.Active.clips.Find(c=>c.id==targetId);
   var track=onTrack?displayedProject.Active.tracks.Find(t=>t.id==targetId):ownerClip==null?null:displayedProject.Active.tracks.Find(t=>t.id==ownerClip.track_id);
   return track!=null&&!track.locked&&track.kind==contract.input_kind;
  }
  void AttachCatalogDrop(Control target){
   if(!catalogDropWiring.Add(target))return;
   target.AllowDrop=true;
   target.DragEnter+=(s,e)=>{
    if(!e.Data.GetDataPresent("Prometheus.EffectId"))return;
    bool onTrack;string owner;int? slot;Panel card;
    e.Effect=CatalogTarget(target,e.Data.GetData("Prometheus.EffectId") as string,new Point(e.X,e.Y),out onTrack,out owner,out slot,out card)?DragDropEffects.Copy:DragDropEffects.None;
   };
   target.DragOver+=(s,e)=>{
    if(!e.Data.GetDataPresent("Prometheus.EffectId"))return;
    bool onTrack;string owner;int? slot;Panel card;
    bool valid=CatalogTarget(target,e.Data.GetData("Prometheus.EffectId") as string,new Point(e.X,e.Y),out onTrack,out owner,out slot,out card);
    e.Effect=valid?DragDropEffects.Copy:DragDropEffects.None;
    SetCatalogHover(valid?card:null,valid&&card!=null&&slot>cardOwners[card].Item3);
   };
   target.DragLeave+=(s,e)=>SetCatalogHover(null,false);
   target.DragDrop+=(s,e)=>{
    if(!e.Data.GetDataPresent("Prometheus.EffectId"))return;
    bool onTrack;string owner;int? slot;Panel card;
    bool valid=CatalogTarget(target,e.Data.GetData("Prometheus.EffectId") as string,new Point(e.X,e.Y),out onTrack,out owner,out slot,out card);
    SetCatalogHover(null,false);
    if(valid&&CatalogEffectDropped!=null)CatalogEffectDropped(onTrack,owner,e.Data.GetData("Prometheus.EffectId") as string,slot);
   };
   foreach(Control child in target.Controls)AttachCatalogDrop(child);
  }
  static string ParameterLabel(string id){switch(id){case "brightness":return "Brillo";case "contrast":return "Contraste";case "saturation":return "Saturación";case "sigma":return "Intensidad";case "amount":return "Cantidad";case "zoom_percent":return "Zoom";case "distance_percent":return "Distancia";case "motion":return "Movimiento";case "direction":return "Dirección";case "degrees":return "Grados";case "frequency":return "Frecuencia";case "similarity":return "Similitud";case "blend":return "Suavidad";case "left":return "Izquierda";case "right":return "Derecha";case "top":return "Arriba";case "bottom":return "Abajo";case "interpolation":return "Interpolación";case "stops":return "Exposición";case "black":return "Nivel de negro";case "intensity":return "Intensidad";case "block":return "Bloque";case "threshold_db":return "Umbral";case "ratio":return "Relación";case "attack_ms":return "Ataque";case "release_ms":return "Liberación";case "makeup_db":return "Ganancia";default:return id;}}
  sealed class WheelSafeComboBox:ComboBox {protected override void WndProc(ref Message message){if(message.Msg==0x20A&&!DroppedDown)return;base.WndProc(ref message);}}
 }
}


