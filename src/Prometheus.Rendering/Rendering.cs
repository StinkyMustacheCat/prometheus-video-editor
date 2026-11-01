using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Threading;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.Runtime.InteropServices;using System.Media;using System.Security.Cryptography;

namespace Prometheus {

 public static class Rendering {

  sealed class MaskInput0104 {public int index;public bool timed,sam2,inverted;public double feather;public string blend="add";}

  static string N(double x){return Tools.N(x);}

  static string Offset(double fraction,int extent){double pixels=fraction*extent;return "(W-w)/2"+(pixels<0?"":"+")+N(pixels);}

  static string OffsetY(double fraction,int extent){double pixels=fraction*extent;return "(H-h)/2"+(pixels<0?"":"+")+N(pixels);}

  public static string GeneratedCacheRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prometheus","GeneratedCache");

  static string PlaceholderImage(Asset asset,int width,int height){

   string key=asset.id+"|"+asset.name+"|"+GeneratedMedia.Note(asset)+"|"+width+"x"+height;

   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,24);

   string directory=GeneratedCacheRoot;Directory.CreateDirectory(directory);

   string file=Path.Combine(directory,hash+".png");if(File.Exists(file))return file;

   string temp=file+".partial-"+Store.Id();

   try{

    using(var image=new Bitmap(width,height))using(var g=Graphics.FromImage(image)){

     g.Clear(Color.FromArgb(55,61,69));g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

     float scale=Math.Min(width/1280f,height/720f);

     using(var edge=new Pen(Color.FromArgb(255,170,95),Math.Max(2,5*scale)))g.DrawRectangle(edge,Math.Max(3,14*scale),Math.Max(3,14*scale),width-Math.Max(6,28*scale),height-Math.Max(6,28*scale));

     using(var small=new Font("Segoe UI",Math.Max(10,22*scale),FontStyle.Bold))using(var large=new Font("Segoe UI",Math.Max(14,46*scale),FontStyle.Bold))using(var noteFont=new Font("Segoe UI",Math.Max(10,24*scale)))using(var titleInk=new SolidBrush(Color.WhiteSmoke))using(var noteInk=new SolidBrush(Color.FromArgb(205,211,218)))using(var accent=new SolidBrush(Color.FromArgb(255,188,126))){

      var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};

      g.DrawString("ESPACIO RESERVADO",small,accent,new RectangleF(24*scale,height*0.16f,width-48*scale,48*scale),center);

      g.DrawString(asset.name,large,titleInk,new RectangleF(48*scale,height*0.30f,width-96*scale,height*0.30f),center);

      string note=GeneratedMedia.Note(asset);if(note.Length>0)g.DrawString(note,noteFont,noteInk,new RectangleF(70*scale,height*0.68f,width-140*scale,height*0.17f),center);

     }

     image.Save(temp,ImageFormat.Png);

    }

    if(File.Exists(file))File.Delete(temp);else File.Move(temp,file);return file;

   }finally{if(File.Exists(temp))File.Delete(temp);}

  }

  static string PendingPreviewImage(int width,int height,bool mgt=false){

   string directory=GeneratedCacheRoot;Directory.CreateDirectory(directory);

   string file=Path.Combine(directory,(mgt?"composicion-mgt-":"vista-ligera-")+width+"x"+height+".png");if(File.Exists(file))return file;

   string temp=file+".partial-"+Store.Id();

   try{using(var image=new Bitmap(width,height))using(var g=Graphics.FromImage(image)){

    g.Clear(Color.FromArgb(38,43,51));g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

    float scale=Math.Min(width/640f,height/360f);

    using(var pen=new Pen(Color.FromArgb(240,165,94),Math.Max(2,3*scale)))g.DrawRectangle(pen,Math.Max(4,18*scale),Math.Max(4,18*scale),width-Math.Max(8,36*scale),height-Math.Max(8,36*scale));

    using(var font=new Font("Segoe UI",Math.Max(14,27*scale),FontStyle.Bold))using(var ink=new SolidBrush(Color.WhiteSmoke))

     g.DrawString(mgt?"PREPARANDO COMPOSICIÓN MGT":"DESCARGANDO VISTA LIGERA",font,ink,new RectangleF(width*0.08f,height*0.38f,width*0.84f,height*0.24f),new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});

    image.Save(temp,ImageFormat.Png);

   }if(File.Exists(file))File.Delete(temp);else File.Move(temp,file);return file;}

   finally{if(File.Exists(temp))File.Delete(temp);}

  }

  public static string Build(Project p,bool video,bool audio,int width,int height,int fps,string output,double seek){return Build(p,video,audio,width,height,fps,output,seek,false);}

  public static string Build(Project p,bool video,bool audio,int width,int height,int fps,string output,double seek,bool preview){

   return Build(p,video,audio,width,height,fps,output,seek,preview,1);

  }

  public static string Build(Project p,bool video,bool audio,int width,int height,int fps,string output,double seek,bool preview,double playbackSpeed){

   return Build(p,video,audio,width,height,fps,output,seek,preview,playbackSpeed,null);

  }

  public static string Build(Project p,bool video,bool audio,int width,int height,int fps,string output,double seek,bool preview,double playbackSpeed,ExportOptions export){

   if(playbackSpeed<0.5||playbackSpeed>3)throw new Exception("Velocidad de reproducción fuera de rango");

   if(export!=null)export.Validate(p);

   var seq=p.Active;if(!seq.transitions.Any(t=>t.preview_expanded))Store.Validate(p);p=NestedMedia0910.Flatten(p);seq=p.Active;double length=export!=null&&export.RenderDuration>0?export.RenderDuration:seq.Length;if(length<=0)throw new Exception("La secuencia está vacía");if(seq.clips.Any(c=>c.enabled&&seq.tracks.Any(t=>t.id==c.track_id&&t.enabled&&(video||t.kind=="audio"))&&FFmpegEffectAdapter.ActiveEffects(p,c).Any(effect=>!FFmpegEffectAdapter.Supports(effect))))throw new Exception("La secuencia contiene un efecto activo sin adaptador de render.");if(p.extensions.Any(x=>x==null||(x.@namespace!="prometheus.import.pending"&&x.@namespace!="prometheus.ui.media-library"&&x.@namespace!="prometheus.ui.media-shelf")||x.version!="1")||seq.extensions.Any(x=>x==null||x.version!="1"||(x.@namespace!="prometheus.ui.track-presentation"&&x.@namespace!="prometheus.sequence-marker"&&x.@namespace!=SequenceExportRange.Namespace&&x.@namespace!=Sequences.ViewNamespace&&x.@namespace!=SubtitleTrack0817.Namespace&&x.@namespace!=SubtitleAnimation0820.Namespace&&x.@namespace!=Ducking01016.Namespace))||p.assets.Any(a=>a.extensions.Any(x=>x==null||(x.@namespace!="prometheus.ui.proxy-preference"&&x.@namespace!="prometheus.web.metadata"&&x.@namespace!="prometheus.ui.media-library"&&x.@namespace!=GeneratedMedia.Namespace&&x.@namespace!=RangeMedia.Namespace&&x.@namespace!=MgtMedia.Namespace&&x.@namespace!="prometheus.media.prep-profile"&&x.@namespace!="prometheus.media.index")||x.version!="1"))||seq.clips.Any(c=>c.extensions.Any(x=>x==null||(x.@namespace!="prometheus.ui.clip-label"&&x.@namespace!=PlaceholderReplacement.IntentNamespace&&x.@namespace!=ShortTail.Namespace&&x.@namespace!=ClipFraming.Namespace&&x.@namespace!=TemporalMasks0823.Namespace&&x.@namespace!=Sam2MaskStore090.Namespace&&x.@namespace!=VectorMaskStack0104.Namespace&&x.@namespace!=DynamicMotion0914.PreviewNamespace&&x.@namespace!=NestedMedia0910.Namespace&&x.@namespace!=NestedRenderGroups01034.Namespace&&x.@namespace!=BlendModes01034.Namespace)||x.version!="1")))throw new Exception("El prototipo conserva extensiones de render desconocidas, pero no puede procesarlas todavía");

   var clips=seq.clips.Where(c=>c.enabled&&seq.tracks.Find(t=>t.id==c.track_id).enabled)

    .Where(c=>seq.tracks.Find(t=>t.id==c.track_id).kind=="video"?video:audio)

    .Where(c=>!((!preview||(SubtitleAnimation0820.PreviewOverlayReady&&SubtitleAnimation0820.Template(seq)!=null))&&SubtitleTrack0817.IsCue(p,c)))

    .Where(c=>{double? missing=ShortTail.MissingStart(p,c);return !missing.HasValue||missing.Value>c.timeline_start.Seconds+0.000001;})

    .Where(c=>{if(!preview)return true;var a=p.assets.Find(x=>x.id==c.asset_id);if(a.source.kind=="generated"||Media.SelectRepresentation(a,true)!=null||RangeMedia.FindCovering(p,a,c)!=null)return true;return (a.source.kind=="web"||a.source.kind=="mgt")&&a.streams.Find(s=>s.id==c.stream_id).kind!="audio";})

    .OrderBy(c=>SubtitleTrack0817.IsCue(p,c)?Int32.MaxValue:seq.tracks.Find(t=>t.id==c.track_id).order).ThenBy(c=>c.timeline_start.Seconds).ThenBy(c=>FFmpegTransitionAdapter.Incoming(seq,c)!=null?1:0).ToList();

   var args=new StringBuilder("-hide_banner -loglevel error -nostdin -y ");var graph=new List<string>();var index=new Dictionary<string,int>();var generatedInputs=new Dictionary<string,int>();var mediaInputs=new Dictionary<string,int>();var chosen=new Dictionary<string,MediaRepresentation>();var pendingWeb=new HashSet<string>();var sourceOffset=new Dictionary<string,double>();int next=0;bool useNvenc=(export!=null&&export.Acceleration=="cpu")?false:CanUseNvenc();

   foreach(var c in clips){var a=p.assets.Find(x=>x.id==c.asset_id);var st=a.streams.Find(x=>x.id==c.stream_id);if(a.source.kind=="generated"){sourceOffset[c.id]=c.source_in.Seconds;

     chosen[c.id]=new MediaRepresentation{stream_ordinals=new Dictionary<string,int>{{st.id,0}}};

     int shared;if(generatedInputs.TryGetValue(a.id+"|"+st.id,out shared)){index[c.id]=shared;continue;}

     generatedInputs[a.id+"|"+st.id]=next;

     if(st.kind=="audio")args.Append("-f lavfi -i "+Tools.Q("anullsrc=r=48000:cl=stereo")+" ");

     else if(GeneratedMedia.Kind(a)=="solid")args.Append("-f lavfi -i "+Tools.Q("color=c=0x"+GeneratedMedia.ColorHex(a)+":s="+width+"x"+height+":r="+fps)+" ");

     else if(GeneratedMedia.IsText(a))args.Append("-loop 1 -framerate "+fps+" -i "+Tools.Q(TextRaster0817.RenderFile(a,width,height))+" ");

     else args.Append("-loop 1 -framerate "+fps+" -i "+Tools.Q(PlaceholderImage(a,width,height))+" ");

    }else{var rep=Media.SelectRepresentation(a,preview);var segment=rep==null?RangeMedia.FindCovering(p,a,c):null;if(segment!=null){rep=new MediaRepresentation{role="final",locator=new Locator{path=segment.path},stream_ordinals=segment.stream_ordinals};sourceOffset[c.id]=c.source_in.Seconds-segment.start;}else sourceOffset[c.id]=c.source_in.Seconds;

     if(rep==null&&preview&&(a.source.kind=="web"||a.source.kind=="mgt")&&st.kind!="audio"){

      pendingWeb.Add(c.id);chosen[c.id]=new MediaRepresentation{stream_ordinals=new Dictionary<string,int>{{st.id,0}}};

      args.Append("-loop 1 -framerate "+fps+" -i "+Tools.Q(PendingPreviewImage(width,height,a.source.kind=="mgt"))+" ");

     }else{if(rep==null)throw new Exception("Medio sin representación local disponible: "+a.name);chosen[c.id]=rep;if(!preview&&(st.kind=="audio"||st.kind=="image")){int sharedMedia;if(mediaInputs.TryGetValue(rep.locator.path+"|"+st.id,out sharedMedia)){index[c.id]=sharedMedia;continue;}mediaInputs[rep.locator.path+"|"+st.id]=next;}if(st.kind=="image")args.Append("-loop 1 -framerate "+fps+" ");else if(preview&&sourceOffset[c.id]>0){var incomingSeek=FFmpegTransitionAdapter.Incoming(seq,c);double prerollSeek=incomingSeek!=null&&!incomingSeek.preview_expanded?FFmpegTransitionAdapter.Preroll(incomingSeek):0;args.Append("-ss "+N(Math.Max(0,sourceOffset[c.id]-prerollSeek*c.speed.Seconds))+" ");}if(!preview&&useNvenc&&st.kind=="video")args.Append("-hwaccel auto ");args.Append("-i "+Tools.Q(rep.locator.path)+" ");}

    }index[c.id]=next++;}

    var maskInputs=new Dictionary<string,int>();var maskLayers=new Dictionary<string,List<MaskInput0104>>();

    if(video)foreach(var clip in clips.Where(c=>(VectorMaskStack0104.All(c).Any(x=>x.mask!=null&&x.mask.enabled)||Sam2MaskStore090.Active(c))&&TemporalMasks0823.Target(c)!="unassigned")){

     var inputs=new List<MaskInput0104>();

     foreach(var layer in VectorMaskStack0104.All(clip).Where(x=>x.mask!=null&&x.mask.enabled)){

      bool timed=layer.id==1&&TemporalMasks0823.HasAnimation(clip);

      if(timed)args.Append("-framerate "+fps+" -start_number 0 -i "+Tools.Q(ClipMaskRaster.RenderSequence(clip,width,height,fps))+" ");

      else args.Append("-loop 1 -framerate "+fps+" -i "+Tools.Q(ClipMaskRaster.RenderFile(layer.mask,width,height))+" ");

      inputs.Add(new MaskInput0104{index=next++,timed=timed,blend=layer.blend});

     }

     if(Sam2MaskStore090.Active(clip)){var data=Sam2MaskStore090.Get(clip);if(!Sam2MaskStore090.Ready(clip)){if(!preview)throw new Exception("Faltan datos de la máscara SAM2 para "+clip.name);}else foreach(var layer in Sam2MaskStore090.Layers(data).Where(x=>x.enabled)){if(data.static_preview)args.Append("-loop 1 -framerate "+fps+" -i "+Tools.Q(Path.Combine(Sam2MaskStore090.LayerDirectory(data,layer),"000000.png"))+" ");else args.Append("-framerate "+data.fps+" -start_number "+Sam2MaskStore090.FirstFrame(clip,data)+" -i "+Tools.Q(Path.Combine(Sam2MaskStore090.LayerDirectory(data,layer),"%06d.png"))+" ");inputs.Add(new MaskInput0104{index=next++,timed=!data.static_preview,sam2=true,inverted=layer.inverted,feather=layer.feather,blend=layer.blend});}}

     if(inputs.Count>0){maskLayers[clip.id]=inputs;maskInputs[clip.id]=inputs[0].index;}

    }

   int mgtFps=MgtTransitionCache.RenderFps(fps);

   var mgtInputs=new Dictionary<string,int>();

   if(video&&export!=null)foreach(var transition in seq.transitions.Where(t=>MgtTransitionCache.IsMgt(t)&&FFmpegTransitionAdapter.Visible(seq,t))){

    if(!MgtTransitionCache.Ready(p,transition,"final",mgtFps,width,height))throw new Exception("Falta procesar la transición MGT: "+transition.id);

    string pattern=Path.Combine(MgtTransitionCache.DirectoryFor(p,transition,"final",mgtFps,width,height),"%06d.png");

    args.Append("-framerate "+mgtFps+" -start_number 0 -i "+Tools.Q(pattern)+" ");

    mgtInputs[transition.id]=next++;

   }

   var inputPads=new Dictionary<string,string>();

   foreach(var group in clips.GroupBy(c=>index[c.id])){

    var members=group.ToList();var first=members[0];var asset=p.assets.Find(a=>a.id==first.asset_id);var stream=asset.streams.Find(s=>s.id==first.stream_id);

    int ordinal;var map=chosen[first.id].stream_ordinals;ordinal=map!=null&&map.TryGetValue(stream.id,out ordinal)?ordinal:stream.ordinal;

    if(members.Count==1){inputPads[first.id]="["+group.Key+":"+ordinal+"]";continue;}

    var pads=members.Select((c,i)=>"[source"+group.Key+"_"+i+"]").ToList();

    graph.Add("["+group.Key+":"+ordinal+"]"+(stream.kind=="audio"?"asplit":"split")+"="+members.Count+String.Join("",pads));

    for(int i=0;i<members.Count;i++)inputPads[members[i].id]=pads[i];

   }

   if(video)graph.Add("color=c=black:s="+width+"x"+height+":r="+fps+":d="+N(length)+",format=rgba[base]");

   if(audio)graph.Add("anullsrc=r=48000:cl=stereo,atrim=duration="+N(length)+"[silence]");

   string current="base";var audios=new List<string>{"[silence]"};var trackAudios=new Dictionary<string,List<string>>();var audioMembers=new List<NestedAudioMember01034>();int k=0;

   var nestedStack=new List<Tuple<NestedRenderGroup01034,string>>();int nestedOrdinal=0;

   foreach(var c in clips){var a=p.assets.Find(x=>x.id==c.asset_id);var st=a.streams.Find(x=>x.id==c.stream_id);var t=seq.tracks.Find(x=>x.id==c.track_id);string input=inputPads[c.id],label="clip"+k;double speed=c.speed.Seconds;

    if(st.kind!="audio"){

     var chain=NestedRenderGroups01034.Read(c);int shared=0;

     while(shared<nestedStack.Count&&shared<chain.Count&&nestedStack[shared].Item1.outer.id==chain[shared].outer.id)shared++;

     while(nestedStack.Count>shared){int last=nestedStack.Count-1;var group=nestedStack[last];nestedStack.RemoveAt(last);current=NestedRenderAdapter01034.Close(graph,p,group.Item1.outer,current,group.Item2,width,height,fps,nestedOrdinal++);}

     for(int depth=shared;depth<chain.Count;depth++){string below=current;current=NestedRenderAdapter01034.Open(graph,width,height,fps,length,nestedOrdinal++);nestedStack.Add(Tuple.Create(chain[depth],below));}

    }

    var incoming=FFmpegTransitionAdapter.Incoming(seq,c);var outgoing=FFmpegTransitionAdapter.Outgoing(seq,c);

    double preroll=incoming!=null&&!incoming.preview_expanded?FFmpegTransitionAdapter.Preroll(incoming):0;

    double renderedStart=c.timeline_start.Seconds-preroll,renderedDuration=c.duration.Seconds+preroll,renderedOffset=sourceOffset[c.id]-preroll*speed;

    if(renderedOffset< -0.000001&&st.kind!="image")throw new Exception("La transición necesita más margen de entrada en "+c.name);

    if(st.kind!="audio"){

     bool still=st.kind=="image"||pendingWeb.Contains(c.id);long firstFrame=(long)Math.Round(renderedStart*60),endFrame=(long)Math.Round((renderedStart+renderedDuration)*60);

     string preparation=still?"setpts=PTS-STARTPTS,trim=duration="+N(renderedDuration+1.0/fps)+",settb=1/60,setpts=round(PTS)+"+firstFrame:

       "setpts=PTS-STARTPTS,trim=start="+N(preview?0:renderedOffset)+":duration="+N(renderedDuration*speed)+",setpts=(PTS-"+N(preview?0:renderedOffset)+"/TB)/"+N(speed)+",settb=1/60000,setpts=PTS+"+(firstFrame*1000);

     var frame=ClipFraming.Read(c);var displayed=a.source.kind=="generated"?new double[]{width,height}:MediaGeometry.DisplaySize(st,width,height);double sourceWidth=displayed[0],sourceHeight=displayed[1];

     double initialScale=ClipKeyframes.Value(c,"transform.scale",c.timeline_start.Seconds,frame.scale);

     double initialScaleY=frame.scaleLinked?initialScale:ClipKeyframes.Value(c,"transform.scale_y",c.timeline_start.Seconds,frame.scaleY);

     var motion=DynamicMotion0914.Read(p,c);

     double baseFactor=frame.mode=="native"?1:frame.mode=="fill"?Math.Max((double)width/sourceWidth,(double)height/sourceHeight):Math.Min((double)width/sourceWidth,(double)height/sourceHeight);

     double basisWidth=sourceWidth*baseFactor,basisHeight=sourceHeight*baseFactor;

     int scaledWidth=Math.Max(2,(int)Math.Round(basisWidth*initialScale/2)*2),scaledHeight=Math.Max(2,(int)Math.Round(basisHeight*initialScaleY/2)*2);

     string geometry=",scale="+scaledWidth+":"+scaledHeight+":flags=bicubic";

     if(motion.HasZoom||ClipKeyframes.IsVarying(c,"transform.scale")||!frame.scaleLinked&&ClipKeyframes.IsVarying(c,"transform.scale_y")){

      string scaleX=ClipKeyframes.Expression(c,"transform.scale",frame.scale,"t");string scaleY=frame.scaleLinked?scaleX:ClipKeyframes.Expression(c,"transform.scale_y",frame.scaleY,"t");

      if(motion.HasZoom){string relative=motion.Scale(c);scaleX="("+scaleX+")*("+relative+")";scaleY="("+scaleY+")*("+relative+")";}

      double largestX=ClipKeyframes.MaxVisibleValue(c,"transform.scale",frame.scale),largestY=frame.scaleLinked?largestX:ClipKeyframes.MaxVisibleValue(c,"transform.scale_y",frame.scaleY);

      int fixedWidth=Math.Max(2,2*((int)Math.Ceiling(basisWidth*largestX*motion.MaxZoom/2)+2)),fixedHeight=Math.Max(2,2*((int)Math.Ceiling(basisHeight*largestY*motion.MaxZoom/2)+2));

      // A fixed transparent surface keeps independent X/Y scaling stable during playback and export.

      // Bound each animated dimension to the transparent surface allocated below. FFmpeg

      // otherwise rejects the entire frame when rounding or a preview preroll exceeds it.

      geometry=",scale=w='min("+(fixedWidth-4)+"\\,max(2\\,round("+N(basisWidth)+"*("+scaleX+"))))':h='min("+(fixedHeight-4)+"\\,max(2\\,round("+N(basisHeight)+"*("+scaleY+"))))':eval=frame,format=rgba,pad=w="+fixedWidth+":h="+fixedHeight+":x='(ow-iw)/2':y='(oh-ih)/2':color=black@0:eval=frame";

     }

     double initialOpacity=ClipKeyframes.Value(c,"transform.opacity",c.timeline_start.Seconds,frame.opacity);

     string opacity=ClipKeyframes.IsVarying(c,"transform.opacity")?",geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*("+ClipKeyframes.Expression(c,"transform.opacity",frame.opacity,"T")+")'":initialOpacity<0.999999?",colorchannelmixer=aa="+N(initialOpacity):"";

     // Resample source frames before evaluating animated filters. A 30 FPS source in a 60 FPS

     // sequence otherwise holds scale/effect values for two output frames at a time.

      string maskTarget=TemporalMasks0823.Target(c);var allEffects=FFmpegEffectAdapter.ActiveEffects(p,c).ToList();var targetedEffect=maskInputs.ContainsKey(c.id)&&maskTarget.StartsWith("effect:",StringComparison.Ordinal)?allEffects.FirstOrDefault(e=>e.id==maskTarget.Substring(7)):null;

      if(maskInputs.ContainsKey(c.id)&&maskTarget.StartsWith("effect:",StringComparison.Ordinal)&&targetedEffect==null)maskInputs.Remove(c.id);

      string tail=geometry+",setsar=1,format=rgba"+opacity+FFmpegTransitionAdapter.VideoFilters(incoming,outgoing,renderedStart);

      if(targetedEffect!=null){string plainSource="mask_plain_source_"+k,effectSource="mask_fx_source_"+k,plainLabel="mask_plain_clip_"+k;

       graph.Add(input+preparation+",fps="+fps+":round=near,split=2["+plainSource+"]["+effectSource+"]");

       graph.Add("["+plainSource+"]null"+FFmpegEffectAdapter.VideoFilters(p,c,allEffects.Where(e=>e.id!=targetedEffect.id))+tail+"["+plainLabel+"]");

       graph.Add("["+effectSource+"]null"+FFmpegEffectAdapter.VideoFilters(p,c,allEffects)+tail+"["+label+"]");

      }else graph.Add(input+preparation+",fps="+fps+":round=near"+FFmpegEffectAdapter.VideoFilters(p,c)+tail+"["+label+"]");

     string xexpr=!ClipKeyframes.IsVarying(c,"transform.x")?Offset(ClipKeyframes.Value(c,"transform.x",c.timeline_start.Seconds,frame.x),width):"(W-w)/2+("+ClipKeyframes.Expression(c,"transform.x",frame.x,"t")+")*W";

     string yexpr=!ClipKeyframes.IsVarying(c,"transform.y")?OffsetY(ClipKeyframes.Value(c,"transform.y",c.timeline_start.Seconds,frame.y),height):"(H-h)/2+("+ClipKeyframes.Expression(c,"transform.y",frame.y,"t")+")*H";

     if(motion.HasPan){string dx=motion.OffsetX(c),dy=motion.OffsetY(c);if(dx!=null)xexpr="("+xexpr+")+("+dx+")*W";if(dy!=null)yexpr="("+yexpr+")+("+dy+")*H";}

     string combined="composed"+k;

     if(maskInputs.ContainsKey(c.id)){

      string background="mask_bg_"+k,placed="mask_placed_"+k,gray="mask_gray_"+k,masked="mask_result_"+k;

       string below=current;if(targetedEffect!=null){string plainCombined="mask_plain_composed_"+k;graph.Add("["+current+"][mask_plain_clip_"+k+"]overlay=x='"+xexpr+"':y='"+yexpr+"':eof_action=pass:repeatlast=0:enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+plainCombined+"]");below=plainCombined;}

      graph.Add("color=c=black@0:s="+width+"x"+height+":r="+fps+":d="+N(length)+",format=rgba["+background+"]");

      graph.Add("["+background+"]["+label+"]overlay=x='"+xexpr+"':y='"+yexpr+"':eof_action=pass:repeatlast=0:enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+placed+"]");

       // Cada capa conserva su origen temporal y se combina antes de aplicar la transparencia.

       string accumulated="mask_acc_"+k+"_0";

       graph.Add("color=c=black:s="+width+"x"+height+":r="+fps+":d="+N(length)+",format=gray["+accumulated+"]");

       int layerNumber=0;foreach(var inputMask in maskLayers[c.id]){

        layerNumber++;string layerLabel="mask_layer_"+k+"_"+layerNumber,nextLabel="mask_acc_"+k+"_"+layerNumber;

        double sigma=Math.Min(100,inputMask.feather*Math.Min(width,height));

        string timing=inputMask.timed?"setpts=PTS-STARTPTS,tpad=start_duration="+N(c.timeline_start.Seconds)+":start_mode=add:stop_mode=clone,trim=duration="+N(length)+",":"trim=duration="+N(length)+",";

        graph.Add("["+inputMask.index+":v]"+timing+(inputMask.sam2?"scale="+width+":"+height+":flags=bilinear,":"")+"format=gray"+(sigma>0.00001?",gblur=sigma="+N(sigma):"")+(inputMask.inverted?",negate":"")+"["+layerLabel+"]");

        string operation=layerNumber>1&&inputMask.blend=="subtract"?"max(0,A-B)":layerNumber>1&&inputMask.blend=="intersect"?"min(A,B)":"max(A,B)";

        graph.Add("["+accumulated+"]["+layerLabel+"]blend=all_expr='"+operation+"'["+nextLabel+"]");accumulated=nextLabel;

       }

       var sam2=Sam2MaskStore090.Get(c);bool invert=VectorMaskStack0104.Get(c).composite_inverted^(sam2!=null&&sam2.enabled&&sam2.composite_inverted);graph.Add("["+accumulated+"]"+(invert?"negate,":"")+"format=gray["+gray+"]");

      graph.Add("["+placed+"]["+gray+"]alphamerge["+masked+"]");

      if(BlendModes01034.Read(c)=="normal")graph.Add("["+below+"]["+masked+"]overlay=x=0:y=0:eof_action=pass:repeatlast=0:enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+combined+"]");

      else combined=BlendModeRender01034.Compose(graph,below,masked,BlendModes01034.Read(c),k,firstFrame,endFrame);

     }else if(BlendModes01034.Read(c)=="normal")graph.Add("["+current+"]["+label+"]overlay=x='"+xexpr+"':y='"+yexpr+"':eof_action="+(still?"repeat":"pass")+":repeatlast="+(still?"1":"0")+(nestedStack.Count>0?":format=auto":"")+":enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+combined+"]");

     else {string empty="blend_empty_"+k,placed="blend_placed_"+k;graph.Add("color=c=black@0:s="+width+"x"+height+":r="+fps+":d="+N(length)+",format=rgba["+empty+"]");graph.Add("["+empty+"]["+label+"]overlay=x='"+xexpr+"':y='"+yexpr+"':eof_action=pass:repeatlast=0:format=auto:enable='gte(round(t*60),"+firstFrame+")*lt(round(t*60),"+endFrame+")'["+placed+"]");combined=BlendModeRender01034.Compose(graph,current,placed,BlendModes01034.Read(c),k,firstFrame,endFrame);}

     current=combined;

    }else{

      var tempo=new List<string>();double factor=speed;while(factor>2){tempo.Add("atempo=2");factor/=2;}while(factor<0.5){tempo.Add("atempo=0.5");factor/=0.5;}if(Math.Abs(factor-1)>0.000001)tempo.Add("atempo="+N(factor));

       graph.Add(input+"asetpts=PTS-STARTPTS,atrim=start="+N(preview?0:renderedOffset)+":duration="+N(renderedDuration*speed)+",asetpts=PTS-"+N(preview?0:renderedOffset)+"/TB,aresample=48000:async=1000:first_pts=0,"+(tempo.Count>0?String.Join(",",tempo)+",":"")+"aformat=channel_layouts=stereo,atrim=duration="+N(renderedDuration)+FFmpegAudioEffectAdapter.AudioFilters(p,c)+",volume="+N(c.gain_db+t.gain_db)+"dB"+(Math.Abs(c.balance)>0.000001?",stereotools=balance_out="+N(c.balance):"")+(Math.Abs(t.balance)>0.000001?",stereotools=balance_out="+N(t.balance):"")+FFmpegTransitionAdapter.AudioFilters(incoming,outgoing,renderedStart)+",adelay="+((long)Math.Round(renderedStart*48000))+"S:all=1["+label+"]");audioMembers.Add(new NestedAudioMember01034{clip=c,label=label,chain=NestedRenderGroups01034.Read(c)});

    }k++;

   }

   while(nestedStack.Count>0){int last=nestedStack.Count-1;var group=nestedStack[last];nestedStack.RemoveAt(last);current=NestedRenderAdapter01034.Close(graph,p,group.Item1.outer,current,group.Item2,width,height,fps,nestedOrdinal++);}

   if(audio)foreach(var renderedAudio in NestedAudioRender01034.Build(graph,p,audioMembers)){audios.Add("["+renderedAudio.label+"]");List<string> lane;if(!trackAudios.TryGetValue(renderedAudio.trackId,out lane)){lane=new List<string>();trackAudios[renderedAudio.trackId]=lane;}lane.Add("["+renderedAudio.label+"]");}

   if(video&&mgtInputs.Count>0)foreach(var transition in seq.transitions.Where(t=>mgtInputs.ContainsKey(t.id)).OrderBy(t=>seq.tracks.Find(track=>track.id==seq.clips.Find(c=>c.id==t.left_clip_id).track_id).order)){

    int first=MgtTransitionCache.StartFrame(transition,mgtFps),last=first+MgtTransitionCache.Frames(transition,mgtFps);

    string image="mgt_image_"+mgtInputs[transition.id],combined="mgt_composed_"+mgtInputs[transition.id];

    graph.Add("["+mgtInputs[transition.id]+":v]format=rgba,setpts=PTS-STARTPTS+"+N(first/(double)mgtFps)+"/TB["+image+"]");

    graph.Add("["+current+"]["+image+"]overlay=eof_action=pass:repeatlast=0:enable='gte(round(t*"+mgtFps+"),"+first+")*lt(round(t*"+mgtFps+"),"+last+")'["+combined+"]");current=combined;

   }

   if(!preview&&video){

    var cues=seq.clips.Where(c=>c.enabled&&seq.tracks.Find(t=>t.id==c.track_id).enabled&&SubtitleTrack0817.IsCue(p,c)).OrderBy(c=>c.timeline_start.Seconds).ToList();

    if(cues.Count>0){

     string ass=TextRaster0817.RenderAss(p,cues,width,height);

     if(ass!=null){

      string subLabel="sub_composed_"+k++;

      graph.Add("["+current+"]subtitles='"+ass.Replace("\\","/").Replace(":","\\:")+"'["+subLabel+"]");

      current=subLabel;

     }

    }

   }

   if(video)graph.Add("["+current+"]trim=start="+N(seek)+",setpts=PTS-STARTPTS,format="+(output=="raw"?"bgra":"yuv420p")+"[vout]");

   if(audio){

    var rules=Ducking01016.All(seq).Where(r=>r.enabled&&trackAudios.ContainsKey(r.target_track_id)&&trackAudios.ContainsKey(r.voice_track_id)).ToList();

    if(rules.Count>0){

     var buses=new Dictionary<string,string>();int busNumber=0;

     foreach(var lane in trackAudios){string bus="duck_bus_"+busNumber++;string combined=lane.Value.Count==1?"anull":"amix=inputs="+lane.Value.Count+":duration=longest:normalize=0";graph.Add(String.Join("",lane.Value)+combined+",apad=pad_dur="+N(length)+",atrim=duration="+N(length)+"["+bus+"]");buses[lane.Key]="["+bus+"]";}

     var sidechains=new Dictionary<string,Queue<string>>();

     foreach(var group in rules.GroupBy(r=>r.voice_track_id)){string source=buses[group.Key];var pads=Enumerable.Range(0,group.Count()+1).Select(i=>"[duck_split_"+busNumber+"_"+i+"]").ToArray();busNumber++;graph.Add(source+"asplit="+pads.Length+String.Join("",pads));buses[group.Key]=pads[0];sidechains[group.Key]=new Queue<string>(pads.Skip(1));}

     foreach(var rule in rules){string ducked="[ducked_"+busNumber+"]";busNumber++;double threshold=Math.Pow(10,rule.threshold_db/20);double ratio=1+Math.Abs(rule.amount_db)/2;graph.Add(buses[rule.target_track_id]+sidechains[rule.voice_track_id].Dequeue()+"sidechaincompress=threshold="+N(threshold)+":ratio="+N(ratio)+":attack="+N(rule.attack_ms)+":release="+N(rule.release_ms)+ducked);buses[rule.target_track_id]=ducked;}

     audios.Clear();audios.Add("[silence]");audios.AddRange(buses.Values);

    }

    string tempo="";double factor=playbackSpeed;while(factor>2){tempo+=",atempo=2";factor/=2;}if(Math.Abs(factor-1)>0.000001)tempo+=",atempo="+N(factor);graph.Add(String.Join("",audios)+"amix=inputs="+audios.Count+":duration=longest:normalize=0,alimiter=limit=0.98:level=false:latency=true,atrim=start="+N(seek)+":duration="+N(length-seek)+",asetpts=PTS-STARTPTS"+tempo+"[aout]");}

   args.Append("-filter_complex_threads "+Math.Min(4,Math.Max(1,Environment.ProcessorCount))+" -filter_complex "+Tools.Q(String.Join(";",graph))+" ");if(video)args.Append("-map \"[vout]\" ");if(audio)args.Append("-map \"[aout]\" ");args.Append("-t "+N(length-seek)+" ");

   if(output=="raw")args.Append("-an -c:v rawvideo -pix_fmt bgra -f rawvideo pipe:1");

   else if(!video)args.Append(output=="audio-raw"?"-c:a pcm_s16le -ar 48000 -ac 2 -f s16le pipe:1":"-c:a pcm_s16le -ar 48000 -ac 2 "+Tools.Q(output));

   else{

    var selected=export??ExportOptions.For(p);

    useNvenc=(selected!=null&&selected.Acceleration=="cpu")?false:CanUseNvenc();

    if(useNvenc){

     string nvPreset="p4";
     if(selected.Preset=="ultrafast"||selected.Preset=="veryfast")nvPreset="p3";
     else if(selected.Preset=="slow")nvPreset="p5";
     args.Append("-c:v h264_nvenc -preset "+nvPreset+" -spatial-aq 1 ");

     if(selected.RateControl=="bitrate")args.Append("-b:v "+selected.TargetBitrateKbps+"k -maxrate "+(int)Math.Round(selected.TargetBitrateKbps*1.3)+"k -bufsize "+(selected.TargetBitrateKbps*2)+"k ");

     else args.Append("-cq "+selected.Crf+" ");

    }else{

     args.Append("-c:v libx264 -preset "+selected.Preset+" ");

     if(selected.RateControl=="bitrate")args.Append("-b:v "+selected.TargetBitrateKbps+"k -maxrate "+(int)Math.Round(selected.TargetBitrateKbps*1.3)+"k -bufsize "+(selected.TargetBitrateKbps*2)+"k ");

     else args.Append("-crf "+selected.Crf+" ");

    }

    args.Append("-pix_fmt yuv420p -r "+fps+" "+(audio?"-c:a aac -b:a "+selected.AudioBitrateKbps+"k ":"")+"-movflags +faststart "+Tools.Q(output));

   }

   return args.ToString();

  }

   public static bool CanUseNvenc(){ return Tools.CanUseNvenc(); }
  static ExportOptions DefaultExport(Project p,string file){var options=ExportOptions.For(p);options.Video=Path.GetExtension(file).Equals(".mp4",StringComparison.OrdinalIgnoreCase);return options;}

  public static void Export(Project p,string file,CancellationToken token){Export(p,file,token,null,DefaultExport(p,file));}

  public static void Export(Project p,string file,CancellationToken token,Action<double> progress){Export(p,file,token,progress,DefaultExport(p,file));}

  public static void Export(Project p,string file,CancellationToken token,Action<double> progress,ExportOptions options){

   if(options==null)throw new ArgumentNullException("options");options.Validate(p);

   string extension=Path.GetExtension(file).ToLowerInvariant();

   if(options.Video&&extension!=".mp4"||!options.Video&&extension!=".wav")throw new Exception("La extensi\u00f3n de salida no coincide con el formato seleccionado");

   string temp=Path.Combine(Path.GetDirectoryName(file),Path.GetFileNameWithoutExtension(file)+".partial-"+Store.Id()+extension);

   string chunkDir=null;

   try{

    token.ThrowIfCancellationRequested();if(progress!=null)progress(0);

    var sources=NestedMedia0910.Flatten(p);var used=sources.Active.clips.Where(c=>c.enabled&&sources.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&(options.Video||t.kind=="audio"))).ToList();

    foreach(var group in used.GroupBy(c=>c.asset_id)){

     token.ThrowIfCancellationRequested();var asset=p.assets.Find(a=>a.id==group.Key);

     if(asset.source.kind=="generated")continue;

     if(Media.SelectRepresentation(asset,options.UsePreview)!=null)Media.Verify(asset,token,options.UsePreview);

     else if((options.UsePreview||options.SelectiveWeb)&&group.All(c=>RangeMedia.FindCovering(sources,asset,c)!=null))RangeMedia.VerifyUsed(sources,asset,token);

     else throw new Exception("Falta "+(options.UsePreview?"una vista ligera o archivo local":options.SelectiveWeb?"un tramo utilizable o el medio completo":"el medio completo")+" para "+asset.name);

    }

    var mgt=options.Video?p.Active.transitions.Where(t=>MgtTransitionCache.IsMgt(t)&&FFmpegTransitionAdapter.Visible(p.Active,t)).OrderBy(t=>t.cut_frame).ToList():new List<TransitionInstance>();

    for(int i=0;i<mgt.Count;i++){token.ThrowIfCancellationRequested();int ordinal=i;MgtTransitionCache.Ensure(p,mgt[i],"final",MgtTransitionCache.RenderFps(options.Fps),options.Width,options.Height,token,percent=>{if(progress!=null)progress((ordinal+percent/100.0)/mgt.Count*20);});}

    double renderBase=mgt.Count>0?20:0;

    double totalDuration=options.RenderDuration>0?options.RenderDuration:p.Active.Length;

    long totalFrames=(long)Math.Round(totalDuration*60);



    if(options.Video&&totalDuration>60){

     var ranges=new List<Tuple<long,long>>();

     long currentStart=0,chunkFrames=60*60;

     while(currentStart<totalFrames){

      long targetEnd=Math.Min(totalFrames,currentStart+chunkFrames);

      bool adjusted=true;

      while(adjusted&&targetEnd<totalFrames){

       adjusted=false;

       foreach(var t in p.Active.transitions.Where(x=>x.enabled)){

        int lead=t.transition_id==TransitionContracts.Dissolve?t.duration_frames:t.transition_id==TransitionContracts.Mgt?(t.duration_frames+1)/2:t.duration_frames/2;

        int trail=t.transition_id==TransitionContracts.Dissolve?0:t.transition_id==TransitionContracts.Mgt?t.duration_frames-lead:t.duration_frames/2;

        int begin=t.cut_frame-lead,finish=t.cut_frame+trail;

        if(targetEnd>begin&&targetEnd<finish){targetEnd=finish;adjusted=true;}

       }

      }

      ranges.Add(Tuple.Create(currentStart,targetEnd));

      currentStart=targetEnd;

     }

     if(ranges.Count>1&&(ranges[ranges.Count-1].Item2-ranges[ranges.Count-1].Item1)<15*60){

      var last=ranges[ranges.Count-1];var prev=ranges[ranges.Count-2];

      ranges.RemoveAt(ranges.Count-1);

      ranges[ranges.Count-1]=Tuple.Create(prev.Item1,last.Item2);

     }

     chunkDir=Path.Combine(Path.GetTempPath(),"prometheus-chunks-"+Store.Id());

     Directory.CreateDirectory(chunkDir);

     string masterAudio=Path.Combine(chunkDir,"master_audio.wav");
     bool hasAudio=p.Active.clips.Any(c=>c.enabled&&p.assets.Any(a=>a.id==c.asset_id&&a.streams.Any(s=>s.id==c.stream_id&&s.kind=="audio")));
     if(hasAudio){
      string audioArgs="-y -hide_banner -loglevel error "+Build(p,false,true,options.Width,options.Height,options.Fps,masterAudio,0,options.UsePreview,1,options);
      using(var aProc=Tools.Start(Tools.FFmpeg,audioArgs)){
       aProc.WaitForExit();
       token.ThrowIfCancellationRequested();
      }
      if(!File.Exists(masterAudio)||new FileInfo(masterAudio).Length==0)hasAudio=false;
     }



     int concurrency=Math.Max(1,Math.Min(2,Environment.ProcessorCount));

     using(var semaphore=new System.Threading.SemaphoreSlim(concurrency)){

      var tasks=new List<System.Threading.Tasks.Task>();

      object sync=new object();

      double[] renderedSeconds=new double[ranges.Count];

      double lastReported=renderBase;

      Exception taskException=null;



      for(int i=0;i<ranges.Count;i++){

       token.ThrowIfCancellationRequested();

       if(taskException!=null)break;

       int chunkIndex=i;long sFrame=ranges[i].Item1,eFrame=ranges[i].Item2;

       double chunkDur=(eFrame-sFrame)/60.0;

       string cOut=Path.Combine(chunkDir,String.Format("chunk_{0:D4}.mp4",chunkIndex));

       semaphore.Wait(token);

       tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(()=>{

        try{

         if(token.IsCancellationRequested||taskException!=null)return;

         var chunkProj=SequenceExportRange.Window(p,sFrame,eFrame);

         var chunkOpts=options.ForWindow(chunkDur);

         string chunkArgs="-nostats -stats_period 0.2 -progress pipe:1 "+Build(chunkProj,true,!hasAudio,options.Width,options.Height,options.Fps,cOut,0,options.UsePreview,1,chunkOpts);

         using(var proc=Tools.Start(Tools.FFmpeg,chunkArgs)){

          string err="";

          proc.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)err+=e.Data+"\n";};

          proc.OutputDataReceived+=(s,e)=>{

           if(e.Data!=null&&e.Data.StartsWith("out_time_us=")){

            long us;if(long.TryParse(e.Data.Substring(12),out us)){

             double sec=Math.Min(chunkDur,us/1000000.0);

             lock(sync){

              renderedSeconds[chunkIndex]=sec;

              double sum=0;for(int k=0;k<renderedSeconds.Length;k++)sum+=renderedSeconds[k];

              double pct=Math.Min(99.0,Math.Max(lastReported,renderBase+(sum/totalDuration)*(100-renderBase)));

              lastReported=pct;

              if(progress!=null)progress(pct);

             }

            }

           }

          };

          proc.BeginErrorReadLine();proc.BeginOutputReadLine();

          using(token.Register(()=>{try{proc.Kill();}catch{}})){

           proc.WaitForExit();token.ThrowIfCancellationRequested();

           if(proc.ExitCode!=0){
             var activeClips=p.Active.clips.Where(c=>c.enabled&&c.timeline_start.Seconds<(eFrame/60.0)&&c.End>(sFrame/60.0)).Select(c=>c.name).Distinct().ToList();
             string clipList=activeClips.Count>0?String.Join(", ",activeClips):"sin clips activos";
             string errDetail=err.Length>1500?err.Substring(err.Length-1500):err;
             throw new Exception(String.Format("Error al exportar fragmento {0} ({1:0.0}s-{2:0.0}s). Medios involucrados: [{3}]. Detalle: {4}",chunkIndex,sFrame/60.0,eFrame/60.0,clipList,errDetail.Trim()));
            }

          }

         }

         lock(sync){

          renderedSeconds[chunkIndex]=chunkDur;

          double sum=0;for(int k=0;k<renderedSeconds.Length;k++)sum+=renderedSeconds[k];

          double pct=Math.Min(99.0,Math.Max(lastReported,renderBase+(sum/totalDuration)*(100-renderBase)));

          lastReported=pct;

          if(progress!=null)progress(pct);

         }

        }catch(Exception ex){

         lock(sync){if(taskException==null)taskException=ex;}

        }finally{semaphore.Release();}

       },token));

      }

      System.Threading.Tasks.Task.WaitAll(tasks.ToArray());

      if(taskException!=null)throw taskException;

     }

     token.ThrowIfCancellationRequested();

     if(progress!=null)progress(99.0);

     string listFile=Path.Combine(chunkDir,"concat_list.txt");

     using(var writer=new StreamWriter(listFile,false,new UTF8Encoding(false))){

      for(int i=0;i<ranges.Count;i++){

       string cOut=Path.Combine(chunkDir,String.Format("chunk_{0:D4}.mp4",i));

       writer.WriteLine("file '"+cOut.Replace("\\","/")+"'");

      }

     }

     string concatArgs;
     if(hasAudio&&File.Exists(masterAudio)){
      concatArgs="-y -hide_banner -loglevel error -f concat -safe 0 -i "+Tools.Q(listFile)+" -i "+Tools.Q(masterAudio)+" -c:v copy -c:a aac -b:a "+options.AudioBitrateKbps+"k -movflags +faststart -shortest "+Tools.Q(temp);
     }else{
      concatArgs="-y -hide_banner -loglevel error -f concat -safe 0 -i "+Tools.Q(listFile)+" -c copy -movflags +faststart "+Tools.Q(temp);
     }

     using(var proc=Tools.Start(Tools.FFmpeg,concatArgs)){

      string err="";proc.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)err+=e.Data+"\n";};

      proc.BeginErrorReadLine();

      using(token.Register(()=>{try{proc.Kill();}catch{}})){

       proc.WaitForExit();token.ThrowIfCancellationRequested();

       if(proc.ExitCode!=0)throw new Exception("Error concatenando fragmentos: "+err);

      }

     }

    }else{

     string args="-nostats -stats_period 0.2 -progress pipe:1 "+Build(p,options.Video,true,options.Width,options.Height,options.Fps,temp,0,options.UsePreview,1,options);

     using(var process=Tools.Start(Tools.FFmpeg,args)){

      string error="";double last=0;

      process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)error+=e.Data+"\n";};

      process.OutputDataReceived+=(s,e)=>{if(e.Data!=null&&e.Data.StartsWith("out_time_us=")){long us;if(long.TryParse(e.Data.Substring(12),out us)){double duration=options.RenderDuration>0?options.RenderDuration:p.Active.Length;double percent=Math.Min(99,Math.Max(last,renderBase+us/1000000.0/duration*(100-renderBase)));last=percent;if(progress!=null)progress(percent);}}};

      process.BeginErrorReadLine();process.BeginOutputReadLine();

      using(token.Register(()=>{try{process.Kill();}catch{}})){process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new Exception(error);}

     }

    }

    token.ThrowIfCancellationRequested();if(!File.Exists(temp)||new FileInfo(temp).Length==0)throw new Exception("El render no generó el archivo final o está vacío.");if(progress!=null)progress(99);

    if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);

    if(progress!=null)progress(100);

   }finally{

    if(chunkDir!=null&&Directory.Exists(chunkDir))try{Directory.Delete(chunkDir,true);}catch{}

    if(File.Exists(temp))try{File.Delete(temp);}catch{}

   }

  }

  static bool ReadFrame(Stream stream,byte[] bytes){int total=0;while(total<bytes.Length){int n=stream.Read(bytes,total,bytes.Length-total);if(n==0)return false;total+=n;}return true;}

  static Bitmap BitmapFrom(byte[] bytes,int w,int h){var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);var data=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);Marshal.Copy(bytes,0,data.Scan0,bytes.Length);b.UnlockBits(data);return b;}

  static double RenderStartFor(Project project,double requested){

   double start=requested;

   foreach(var transition in project.Active.transitions.Where(t=>t.enabled)){

    double cut=transition.cut_frame/60.0,duration=transition.duration_frames/60.0;

    double begin=transition.transition_id==TransitionContracts.Dissolve?cut-duration:cut-duration/2;

    double end=transition.transition_id==TransitionContracts.Dissolve?cut:cut+duration/2;

    if(requested>=begin-0.000001&&requested<=end+0.000001)start=Math.Min(start,begin);

   }

   return Math.Max(0,start);

  }

  public static Project PreviewWindow(Project original,double from,double duration){

   Store.Validate(original);

   double end=Math.Min(original.Active.Length,from+duration);var p=Store.Copy(original);var seq=p.Active;

   seq.clips.RemoveAll(c=>{var track=seq.tracks.Find(t=>t.id==c.track_id);if(!c.enabled||(track!=null&&!track.enabled))return true;var incoming=FFmpegTransitionAdapter.Incoming(seq,c);double effectiveStart=c.timeline_start.Seconds-FFmpegTransitionAdapter.Preroll(incoming);return c.End<=from+0.000001||effectiveStart>=end-0.000001;});

   foreach(var c in seq.clips){var track=seq.tracks.Find(t=>t.id==c.track_id);double grid=track.kind=="audio"?48000:60;double oldStart=c.timeline_start.Seconds;var incoming=FFmpegTransitionAdapter.Incoming(seq,c);double effectiveStart=oldStart-FFmpegTransitionAdapter.Preroll(incoming);double visibleStart=Math.Max(effectiveStart,from);double visibleEnd=Math.Min(c.End,end);double delta=visibleStart-oldStart;double newDuration=Math.Max(1.0/grid,Math.Floor((visibleEnd-visibleStart)*grid+0.000001)/grid);

    if(track.kind=="video")DynamicMotion0914.ShiftPreviewWindow(c,from);

    c.timeline_start=track.kind=="audio"?Rational.Time(visibleStart-from):Rational.Frame(visibleStart-from);

    var asset=p.assets.Find(a=>a.id==c.asset_id);var stream=asset.streams.Find(s=>s.id==c.stream_id);

    // Still images have no decoder seek, but their animation uses source time too.

    // Advance that clock with the preview window so image keyframes match export.

    c.source_in=Rational.Time(c.source_in.Seconds+delta*c.speed.Seconds);

    c.duration=track.kind=="audio"?Rational.Time(newDuration):Rational.Frame(newDuration);

   }

   seq.clips.RemoveAll(c=>c.duration.Seconds<=0);

   seq.link_groups.RemoveAll(g=>{g.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));return g.clip_ids.Count<2;});

   seq.edit_groups.RemoveAll(g=>{g.clip_ids.RemoveAll(id=>!seq.clips.Any(c=>c.id==id));return g.clip_ids.Count<2;});

   seq.transitions.RemoveAll(t=>{double cut=t.cut_frame/60.0,d=t.duration_frames/60.0;double begin=t.transition_id==TransitionContracts.Dissolve?cut-d:cut-d/2;double finish=t.transition_id==TransitionContracts.Dissolve?cut:cut+d/2;return !t.enabled||finish<=from+0.000001||begin>=end-0.000001;});

   foreach(var transition in seq.transitions){transition.cut_frame-=(int)Math.Round(from*60);transition.preview_expanded=true;}

   return p;

  }

  public static Bitmap Frame(Project p,double time,CancellationToken token){var size=SequenceCanvas.PreviewSize(p.Active,640);return Frame(p,time,token,size[0],size[1]);}

  public static Bitmap Frame(Project p,double time,CancellationToken token,int width,int height){return FrameExact(p,time,token,width,height,true);}

  public static Bitmap FrameExact(Project p,double time,CancellationToken token,int width,int height,bool preview){if(p.Active.Length<=0||time>=p.Active.Length)return new Bitmap(width,height);double requested=Math.Floor(Math.Min(Math.Max(0,time),Math.Max(0,p.Active.Length-1.0/60))*60+0.000001)/60.0;double start=RenderStartFor(p,requested);double offset=requested-start;var window=PreviewWindow(p,start,offset+1.0/60);if(window.Active.clips.Count==0)return new Bitmap(width,height);Media.VerifySources(NestedMedia0910.Flatten(window),token,preview);string args=Build(window,true,false,width,height,60,"raw",offset,preview);args=args.Replace("-an -c:v rawvideo","-frames:v 1 -an -c:v rawvideo");using(var process=Tools.Start(Tools.FFmpeg,args)){string error="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)error+=e.Data+"\n";};process.BeginErrorReadLine();using(token.Register(()=>{try{process.Kill();}catch{}})){byte[] bytes=new byte[width*height*4];bool ok=ReadFrame(process.StandardOutput.BaseStream,bytes);process.WaitForExit();token.ThrowIfCancellationRequested();if(!ok||process.ExitCode!=0)throw new Exception(error);var bitmap=BitmapFrom(bytes,width,height);return preview?MgtTransitionCache.Apply(p,bitmap,requested,60,width,height):bitmap;}}}

  static double PreviewElapsed(WaveOutPcm audio,Stopwatch clock){var position=audio==null?(double?)null:audio.PlayedSeconds;return position??clock.Elapsed.TotalSeconds;}

  static double WaitPreviewTime(WaveOutPcm audio,Stopwatch clock,double target,CancellationToken token){double elapsed=PreviewElapsed(audio,clock);while(elapsed<target-0.001){if(audio!=null&&(audio.Finished||clock.Elapsed.TotalSeconds-target>0.75))return clock.Elapsed.TotalSeconds;int delay=(int)Math.Max(1,Math.Min(15,Math.Ceiling((target-elapsed)*1000)));if(token.WaitHandle.WaitOne(delay))token.ThrowIfCancellationRequested();elapsed=PreviewElapsed(audio,clock);}return elapsed;}

  public static void Play(Project p,double from,string tempDir,CancellationToken token,Action<Bitmap,double> frame,Action<string> status){Play(p,from,tempDir,token,frame,status,15,640,360);}

  public static void Play(Project p,double from,string tempDir,CancellationToken token,Action<Bitmap,double> frame,Action<string> status,int fps,int width,int height){

   Play(p,from,tempDir,token,frame,status,fps,width,height,1);

  }

  public static void Play(Project p,double from,string tempDir,CancellationToken token,Action<Bitmap,double> frame,Action<string> status,int fps,int width,int height,double playbackSpeed){

   if(playbackSpeed<0.5||playbackSpeed>3)throw new Exception("Velocidad de reproducción fuera de rango");

   if(p.Active.clips.Count(c=>c.enabled&&p.Active.tracks.Any(t=>t.id==c.track_id&&t.kind=="video"&&t.enabled))>40){PlayDense(p,from,tempDir,token,frame,status,fps,width,height,playbackSpeed);return;}

   double seek=Math.Min(from,Math.Max(0,p.Active.Length-1.0/60));double start=RenderStartFor(p,seek),renderSeek=seek-start;var window=PreviewWindow(p,start,p.Active.Length-start);Directory.CreateDirectory(tempDir);string wav=Path.Combine(tempDir,"preview-"+Store.Id()+".wav");SoundPlayer player=null;WaveOutPcm audio=null;

   try{var soundProject=NestedMedia0910.Flatten(window);Media.VerifySources(soundProject,token,true);bool sound=soundProject.Active.clips.Any(c=>{if(!c.enabled||!soundProject.Active.tracks.Find(t=>t.id==c.track_id).enabled||soundProject.Active.tracks.Find(t=>t.id==c.track_id).kind!="audio")return false;var a=window.assets.Find(x=>x.id==c.asset_id);return a.source.kind=="generated"||Media.SelectRepresentation(a,true)!=null||RangeMedia.FindCovering(window,a,c)!=null;});if(sound){status("Iniciando audio de vista previa…");try{audio=new WaveOutPcm(Build(window,false,true,640,360,15,"audio-raw",renderSeek,true,playbackSpeed),token);}catch(OperationCanceledException){throw;}catch{status("Usando salida de audio alternativa…");Tools.Run(Tools.FFmpeg,Build(window,false,true,640,360,15,wav,renderSeek,true,playbackSpeed),token);player=new SoundPlayer(wav);player.Load();}}

    status("Vista previa "+width+"×"+height+" · hasta "+fps+" FPS · ×"+N(playbackSpeed)+"; montaje "+p.Active.width+"×"+p.Active.height+" a 60 FPS");using(var process=Tools.Start(Tools.FFmpeg,Build(window,true,false,width,height,fps,"raw",renderSeek,true))){string error="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)error+=e.Data+"\n";};process.BeginErrorReadLine();using(token.Register(()=>{try{process.Kill();}catch{}})){var clock=new Stopwatch();var bytes=new byte[width*height*4];long count=0;while(ReadFrame(process.StandardOutput.BaseStream,bytes)){token.ThrowIfCancellationRequested();if(!clock.IsRunning){if(audio!=null)audio.Start();else if(player!=null)player.Play();clock.Start();}if(audio!=null)audio.Check();double target=count/(double)(fps*playbackSpeed);double elapsed=WaitPreviewTime(audio,clock,target,token);if(elapsed-target<0.20)frame(MgtTransitionCache.Apply(p,BitmapFrom(bytes,width,height),seek+count/(double)fps,60,width,height),seek+count/(double)fps);count++;}process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new Exception(error);}}

   }finally{if(audio!=null)audio.Dispose();if(player!=null){player.Stop();player.Dispose();}try{if(File.Exists(wav))File.Delete(wav);}catch{}}

  }

  // A dense montage should not make FFmpeg evaluate every later cut to display the next frame.

  static void PlayDense(Project p,double from,string tempDir,CancellationToken token,Action<Bitmap,double> frame,Action<string> status,int fps,int width,int height,double playbackSpeed){

   double seek=Math.Min(from,Math.Max(0,p.Active.Length-1.0/60)),audioStart=RenderStartFor(p,seek),audioSeek=seek-audioStart;

   var audioWindow=PreviewWindow(p,audioStart,p.Active.Length-audioStart);Directory.CreateDirectory(tempDir);

   string wav=Path.Combine(tempDir,"preview-"+Store.Id()+".wav");SoundPlayer player=null;WaveOutPcm audio=null;

   try{

    Media.VerifySources(NestedMedia0910.Flatten(audioWindow),token,true);

    var soundProject=NestedMedia0910.Flatten(audioWindow);bool sound=soundProject.Active.clips.Any(c=>c.enabled&&soundProject.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&t.kind=="audio"));

    if(sound){status("Iniciando audio de vista previa…");try{audio=new WaveOutPcm(Build(audioWindow,false,true,640,360,15,"audio-raw",audioSeek,true,playbackSpeed),token);}catch(OperationCanceledException){throw;}catch{Tools.Run(Tools.FFmpeg,Build(audioWindow,false,true,640,360,15,wav,audioSeek,true,playbackSpeed),token);player=new SoundPlayer(wav);player.Load();}}

    var clock=new Stopwatch();long globalFrames=0;double cursor=seek;

    while(cursor<p.Active.Length-0.5/fps){

     token.ThrowIfCancellationRequested();double planned=Math.Min(30,p.Active.Length-cursor),start=RenderStartFor(p,cursor),offset=cursor-start;

     var window=PreviewWindow(p,start,offset+planned);bool hasVideo=window.Active.clips.Any(c=>c.enabled&&window.Active.tracks.Any(t=>t.id==c.track_id&&t.enabled&&t.kind=="video"));

     if(!hasVideo){int blackFrames=Math.Max(1,(int)Math.Round(planned*fps));for(int i=0;i<blackFrames;i++){token.ThrowIfCancellationRequested();if(!clock.IsRunning){if(audio!=null)audio.Start();else if(player!=null)player.Play();clock.Start();}if(audio!=null)audio.Check();double target=globalFrames/(double)(fps*playbackSpeed);double elapsed=WaitPreviewTime(audio,clock,target,token);double at=cursor+i/(double)fps;if(elapsed-target<0.20)frame(MgtTransitionCache.Apply(p,new Bitmap(width,height),at,60,width,height),at);globalFrames++;}cursor+=blackFrames/(double)fps;continue;}

     status("Vista previa por tramos · "+N(cursor)+" s · "+width+"×"+height);

     int read=0;using(var process=Tools.Start(Tools.FFmpeg,Build(window,true,false,width,height,fps,"raw",offset,true))){string error="";process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)error+=e.Data+"\n";};process.BeginErrorReadLine();using(token.Register(()=>{try{process.Kill();}catch{}})){var bytes=new byte[width*height*4];while(ReadFrame(process.StandardOutput.BaseStream,bytes)){token.ThrowIfCancellationRequested();if(!clock.IsRunning){if(audio!=null)audio.Start();else if(player!=null)player.Play();clock.Start();}if(audio!=null)audio.Check();double target=globalFrames/(double)(fps*playbackSpeed);double elapsed=WaitPreviewTime(audio,clock,target,token);double at=cursor+read/(double)fps;if(elapsed-target<0.20)frame(MgtTransitionCache.Apply(p,BitmapFrom(bytes,width,height),at,60,width,height),at);read++;globalFrames++;}process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0)throw new Exception(error);}}

     if(read==0)throw new Exception("No se pudo reproducir el tramo del montaje.");cursor+=read/(double)fps;

    }

   }finally{if(audio!=null)audio.Dispose();if(player!=null){player.Stop();player.Dispose();}try{if(File.Exists(wav))File.Delete(wav);}catch{}}

  }

 }

}





















