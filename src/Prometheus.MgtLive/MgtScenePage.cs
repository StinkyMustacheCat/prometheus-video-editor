using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.MgtLive;

/// <summary>Builds one persistent CEF scene. Frame changes only call renderAt.</summary>
internal static class MgtScenePage
{
    // Playback contract from the MGT engine. Index the cues once per scene.
    internal const string SubtitleContext = """
        function prepareSubtitles(data) {
          const cues = data?.cues ?? [];
          const words = cues.flatMap(cue => cue.words ?? []);
          let latestEnd = 0;
          const ends = cues.map(cue => {
            cue.words?.forEach(Object.freeze);
            if (cue.words) Object.freeze(cue.words);
            Object.freeze(cue);
            latestEnd = Math.max(latestEnd, cue.end);
            return latestEnd;
          });
          return Object.freeze({
            cues: Object.freeze(cues), words: Object.freeze(words),
            timing: data?.timing ?? 'cue', tail: data?.tail ?? .25,
            at(time, tail = 0) {
              tail = Math.max(0, Math.min(5, Number(tail) || 0));
              let lo = 0, hi = cues.length;
              while (lo < hi) {
                const mid = (lo + hi) >>> 1;
                if (cues[mid].start <= time) lo = mid + 1;
                else hi = mid;
              }
              const found = [];
              for (let i = lo - 1; i >= 0 && ends[i] + tail > time; i--)
                if (cues[i].end + tail > time) found.push(cues[i]);
              return found.reverse();
            }
          });
        }
        """;

    public static string Build(string source, int width, int height, int? designWidth = null, int? designHeight = null, bool liveSubtitles = false)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var logicalWidth = designWidth ?? width;
        var logicalHeight = designHeight ?? height;
        if (logicalWidth <= 0 || logicalHeight <= 0) throw new ArgumentOutOfRangeException(nameof(designWidth));
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        if (!root.TryGetProperty("html", out var html) || html.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("css", out var css) || css.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("render", out var render) || render.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("La composición MGT no declara html, css y render.");

        var definition = root.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
        definition["render"] = JsonSerializer.SerializeToElement(MgtRenderScript.FunctionBody(render.GetString()!));
        var embedded = JsonSerializer.Serialize(definition, new JsonSerializerOptions {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default
        }).Replace("<", "\\u003C", StringComparison.Ordinal);
        return """
            <!doctype html><html><head><meta charset="utf-8"><style>
            html,body{margin:0;width:__WIDTH__px;height:__HEIGHT__px;overflow:hidden;background:transparent}
            #mgt-root{position:relative;width:__DESIGN_WIDTH__px;height:__DESIGN_HEIGHT__px;overflow:hidden;transform:scale(__SCALE_X__,__SCALE_Y__);transform-origin:top left}
            </style></head><body><div id="mgt-root"></div><script>
            (()=>{
              'use strict';
              __SUBTITLE_CONTEXT__
              const source=__SOURCE__;
              const root=document.getElementById('mgt-root');
              root.innerHTML=source.html;
              const style=document.createElement('style');style.textContent=source.css;document.head.append(style);
              const draw=new Function('ctx',source.render);
              let subtitles=prepareSubtitles(source.subtitles);
              const liveSubtitles=__LIVE_SUBTITLES__;
              if(liveSubtitles)window.mgtSetSubtitles=data=>{subtitles=prepareSubtitles(data);};
              const defaults=Object.fromEntries(source.parameters.map(p=>[p.id,p.default]));
              const images=Object.fromEntries(Object.entries(source.resources??{}).map(([id,resource])=>{
                const src='data:'+resource.mime+';base64,'+resource.data;
                const element=new Image();element.src=src;
                return [id,{src,width:resource.width,height:resource.height,element}];
              }));
              const seed=source.seed??1;
              const duration=source.recommended?.duration??3;
              const fps=60;
              const count=Math.max(1,Math.round(duration*fps));
              window.mgtPaintToken=-1;
              window.renderAt=(frame,overrides={},token=0,clock=null)=>{
                const liveClock=liveSubtitles&&clock;
                if(liveClock&&(!Number.isFinite(clock.time)||clock.time<0||!Number.isFinite(clock.fps)||clock.fps<=0||!Number.isFinite(clock.duration)||clock.duration<=0))throw Error('Reloj de subtítulos inválido');
                if(!Number.isInteger(frame)||frame<0||frame>=count)throw Error('Fotograma MGT fuera de rango');
                const params={...defaults,...overrides};
                const at=liveClock?clock.time:frame/fps,rate=liveClock?clock.fps:fps,len=liveClock?clock.duration:duration;
                const ctx={root,params,subtitles,width:__DESIGN_WIDTH__,height:__DESIGN_HEIGHT__,frame:liveClock?Math.round(at*rate):frame,fps:rate,
                  time:at,duration:len,progress:liveClock?Math.min(1,Math.max(0,at/Math.max(1/rate,len-1/rate))):frame/Math.max(1,count-1),seed,
                  editingStop:null,random(index){let n=((seed>>>0)^Math.imul((index>>>0)+1,0x9e3779b1))>>>0;
                    n=Math.imul(n^(n>>>16),0x21f0aaad);n=Math.imul(n^(n>>>15),0x735a2d97);
                    return ((n^(n>>>15))>>>0)/4294967296;},image(id){return images[id]??null;}};
                for(const node of root.querySelectorAll('img[data-mgt-image]')){
                  const id=params[node.getAttribute('data-mgt-image')];
                  const image=images[id];
                  node.style.display=image?'':'none';
                  if(image&&node.src!==image.src)node.src=image.src;
                }
                draw(ctx);
                for(const animation of document.getAnimations()){
                  animation.pause();animation.currentTime=at*1000;
                }
                requestAnimationFrame(()=>{window.mgtPaintToken=token;});
                return frame;
              };
              window.mgtSceneReady=false;
              Promise.all(Object.values(images).map(image=>image.element.decode().catch(()=>{})))
                .then(()=>document.fonts.ready).then(()=>{window.renderAt(0);window.mgtSceneReady=true;});
            })();
            </script></body></html>
            """.Replace("__WIDTH__", width.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__LIVE_SUBTITLES__", liveSubtitles ? "true" : "false", StringComparison.Ordinal)
               .Replace("__HEIGHT__", height.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__DESIGN_WIDTH__", logicalWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__DESIGN_HEIGHT__", logicalHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SCALE_X__", ((double)width / logicalWidth).ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SCALE_Y__", ((double)height / logicalHeight).ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
               .Replace("__SUBTITLE_CONTEXT__", SubtitleContext, StringComparison.Ordinal)
               .Replace("__SOURCE__", embedded, StringComparison.Ordinal);
    }
}
