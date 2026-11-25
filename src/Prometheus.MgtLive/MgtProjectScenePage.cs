using System.IO.Compression;
using System.Text.Json;

namespace Prometheus.MgtLive;

/// <summary>Live CEF scene for the supported MGT project contract: motion title and composition lanes.</summary>
internal static class MgtProjectScenePage
{
    public static string Build(string path, int width, int height, int designWidth, int designHeight)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("project.json") ?? throw new InvalidDataException("Falta project.json.");
        using var input = new StreamReader(entry.Open());
        using var document = JsonDocument.Parse(input.ReadToEnd());
        var embedded = JsonSerializer.Serialize(document.RootElement).Replace("<", "\\u003C", StringComparison.Ordinal);
        return """
            <!doctype html><html><head><meta charset="utf-8"><style>
            html,body{margin:0;width:__WIDTH__px;height:__HEIGHT__px;overflow:hidden;background:transparent}
            #mgt-root{position:relative;width:__DESIGN_WIDTH__px;height:__DESIGN_HEIGHT__px;overflow:hidden;transform:scale(__SCALE_X__,__SCALE_Y__);transform-origin:top left}
            #mgt-base-title{position:absolute;transform:translate(-50%,-50%);white-space:pre;text-align:center;font-weight:400;font-style:normal;line-height:1.15;font-kerning:normal}
            </style></head><body><div id="mgt-root"><div id="mgt-base-title"></div></div><script>
            (()=>{
              'use strict';
              __SUBTITLE_CONTEXT__
              const project=__SOURCE__,state=project.snapshot.state;
              const root=document.getElementById('mgt-root'),title=document.getElementById('mgt-base-title');
              root.style.backgroundColor=state.background;
              title.textContent=state.text;
              Object.assign(title.style,{fontFamily:state.font,fontSize:state.size+'px',color:state.color});
              const timelineFps=60;
              const fps=Math.min(120,Math.max(1,Number(project.settings?.fps)||60));
              const count=Math.max(1,Math.round(state.duration*timelineFps));
              const clips=[];
              for(const lane of project.snapshot.tracks.overlayLanes){
                for(const clip of lane.clips){
                  const host=document.createElement('div');
                  Object.assign(host.style,{position:'absolute',inset:'0',overflow:'hidden',display:'none'});
                  root.append(host);
                  const shadow=host.attachShadow({mode:'open'});
                  const style=document.createElement('style');style.textContent=clip.document.css;shadow.append(style);
                  const clipRoot=document.createElement('div');
                  Object.assign(clipRoot.style,{position:'relative',width:'100%',height:'100%',overflow:'hidden'});
                  clipRoot.innerHTML=clip.document.html;shadow.append(clipRoot);
                  const draw=new Function('ctx',clip.document.render);
                  const subtitles=prepareSubtitles(clip.document.subtitles);
                  const defaults=Object.fromEntries(clip.document.parameters.map(p=>[p.id,p.default]));
                  const images=Object.fromEntries(Object.entries(clip.document.resources??{}).map(([id,resource])=>{
                    const src='data:'+resource.mime+';base64,'+resource.data;
                    const element=new Image();element.src=src;
                    return [id,{src,width:resource.width,height:resource.height,element}];
                  }));
                  clips.push({clip,host,clipRoot,draw,defaults,images,subtitles});
                }
              }
              window.mgtPaintToken=-1;
              window.renderAt=(frame,overrides={},token=0)=>{
                if(!Number.isInteger(frame)||frame<0||frame>=count)throw Error('Fotograma MGT fuera de rango');
                const projectFrame=Math.min(Math.max(0,Math.round(state.duration*fps)-1),Math.round(frame*fps/timelineFps));
                const progress=frame/Math.max(1,count-1);
                const eased=state.easing==='smooth'?progress*progress*(3-2*progress):progress;
                title.style.left=(state.start.x+(state.end.x-state.start.x)*eased)+'%';
                title.style.top=(state.start.y+(state.end.y-state.start.y)*eased)+'%';
                for(const item of clips){
                  const {clip,host,clipRoot,draw,defaults,images,subtitles}=item;
                  const start=clip.start??0,frames=clip.frames??count;
                  const active=projectFrame>=start&&projectFrame<start+frames;
                  host.style.display=active?'block':'none';
                  if(!active)continue;
                  const local=Math.max(0,Math.round((projectFrame-start)*(clip.sourceFrames??frames)/Math.max(1,frames))+(clip.inFrame??0));
                  const duration=clip.document.recommended?.duration??frames/fps;
                  const localCount=Math.max(1,Math.round(duration*fps));
                  const sourceFrame=Math.min(localCount-1,local);
                  const prefix='clip/'+clip.id+'/';
                  const selected=Object.fromEntries(Object.entries(overrides).filter(([key])=>key.startsWith(prefix))
                    .map(([key,value])=>[key.slice(prefix.length),value]));
                  const params={...defaults,...(clip.params??{}),...selected};
                  for(const node of clipRoot.querySelectorAll('img[data-mgt-image]')){
                    const image=images[params[node.getAttribute('data-mgt-image')]];
                    node.style.display=image?'':'none';
                    if(image&&node.src!==image.src)node.src=image.src;
                  }
                  const seed=clip.document.seed??1;
                  const ctx={root:clipRoot,params,subtitles,width:__DESIGN_WIDTH__,height:__DESIGN_HEIGHT__,frame:sourceFrame,fps,
                    time:sourceFrame/fps,duration,progress:sourceFrame/Math.max(1,localCount-1),seed,
                    editingStop:null,random(index){let n=((seed>>>0)^Math.imul((index>>>0)+1,0x9e3779b1))>>>0;
                      n=Math.imul(n^(n>>>16),0x21f0aaad);n=Math.imul(n^(n>>>15),0x735a2d97);
                      return ((n^(n>>>15))>>>0)/4294967296;},image(id){return images[id]??null;}};
                  draw(ctx);
                }
                for(const animation of document.getAnimations()){animation.pause();animation.currentTime=frame*1000/timelineFps;}
                requestAnimationFrame(()=>{window.mgtPaintToken=token;});
                return frame;
              };
              window.mgtSceneReady=false;
              Promise.all(clips.flatMap(item=>Object.values(item.images).map(image=>image.element.decode().catch(()=>{}))))
                .then(()=>document.fonts.ready).then(()=>{window.renderAt(0);window.mgtSceneReady=true;});
            })();
            </script></body></html>
            """.Replace("__WIDTH__", width.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__HEIGHT__", height.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__DESIGN_WIDTH__", designWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__DESIGN_HEIGHT__", designHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SCALE_X__", ((double)width / designWidth).ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SCALE_Y__", ((double)height / designHeight).ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SUBTITLE_CONTEXT__", MgtScenePage.SubtitleContext, StringComparison.Ordinal)
               .Replace("__SOURCE__", embedded, StringComparison.Ordinal);
    }
}
