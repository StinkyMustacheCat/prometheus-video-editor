using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

/// <summary>Runs a bounded script against a recording canvas. Pixel data stays outside JavaScript.</summary>
public sealed class MgtGpuCanvasProgram
{
    private static readonly Regex Unsupported = new(
        @"\b(?:eval|Function|constructor|__proto__|prototype|import|Date|performance|globalThis|fetch|WebSocket|XMLHttpRequest|setTimeout|setInterval|requestAnimationFrame|async|await|Promise|Atomics|SharedArrayBuffer|Proxy|Reflect)\b|Math\s*\.\s*random",
        RegexOptions.CultureInvariant);
    private readonly MgtGpuTransition _definition;
    private readonly Engine _engine;
    private readonly JsValue _invoke;

    public MgtGpuCanvasProgram(MgtGpuTransition definition)
    {
        definition.Validate();
        if (Unsupported.IsMatch(definition.Render))
            throw new InvalidOperationException("La transición usa una operación de JavaScript fuera del perfil GPU.");
        _definition = definition;
        _engine = new Engine(options =>
        {
            options.Strict();
            options.MaxStatements(50_000).LimitRecursion(48).LimitMemory(24_000_000)
                .TimeoutInterval(TimeSpan.FromMilliseconds(160));
            options.Constraints.MaxArraySize = 32_768;
            options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(30);
            options.Constraints.PromiseTimeout = TimeSpan.FromMilliseconds(30);
        });
        _invoke = _engine.Evaluate(Recorder.Replace("/* RENDER */", definition.Render, StringComparison.Ordinal));
    }

    public JsonDocument Run(int width, int height, double fps, int frames, int index,
        IReadOnlyDictionary<string, JsonElement>? overrides = null)
    {
        if (width < 1 || height < 1 || frames < 2 || index < 0 || index >= frames || fps <= 0)
            throw new ArgumentException("La solicitud de fotograma MGT no es válida.");
        var values = _definition.Parameters.ToDictionary(p => p.Id, p =>
            overrides != null && overrides.TryGetValue(p.Id, out var option) && MgtGpuTransition.Accepts(p, option)
                ? option : p.Default);
        var json = JsonSerializer.Serialize(values);
        var commands = _engine.Invoke(_invoke, width, height, fps, frames, index, _definition.Seed,
            _definition.Recommended.Duration, json).AsString();
        if (commands.Length > 512 * 1024)
            throw new InvalidOperationException("El fotograma MGT excede el presupuesto de dibujo.");
        return JsonDocument.Parse(commands);
    }

    public static void Check(MgtGpuTransition definition)
    {
        var program = new MgtGpuCanvasProgram(definition);
        // Exercise endpoints and interleaved seeks with both common orientations.
        foreach (var (width, height) in new[] { (1920, 1080), (1080, 1920) })
        {
            using var baseline = program.Run(width, height, 60, 60, 30);
            foreach (var frame in new[] { 0, 59, 12, 47 })
                using (program.Run(width, height, 60, 60, frame)) { }
            using var repeated = program.Run(width, height, 60, 60, 30);
            if (baseline.RootElement.GetRawText() != repeated.RootElement.GetRawText())
                throw new InvalidOperationException("La transición depende del orden de los fotogramas.");
        }
    }

    private const string Recorder = """
        (() => {
          const draw = (ctx) => {
            /* RENDER */
          };
          const properties = new Set(['globalAlpha','globalCompositeOperation','fillStyle','strokeStyle',
            'lineWidth','shadowBlur','shadowColor','shadowOffsetX','shadowOffsetY']);
          const methods = ['save','restore','translate','rotate','scale','transform','beginPath','closePath',
            'rect','moveTo','lineTo','arc','clip','stroke','fill','fillRect','clearRect'];
          const number = Number.isFinite;
          const emit = (list, op, args) => {
            if(list.length >= 2048) throw Error('Demasiadas operaciones en un fotograma MGT.');
            if(args.some(v => typeof v === 'number' && (!number(v) || Math.abs(v) > 1000000)))
              throw Error('Coordenada MGT fuera de rango.');
            list.push([op, ...args]);
          };
          const gradient = (kind,args) => {
            if(args.length !== (kind === 1 ? 4 : 6) ||
              args.some(v => !number(v) || Math.abs(v)>1000000) ||
              (kind === 2 && (args[2] < 0 || args[5] <= args[2]))) throw Error('Gradiente MGT inválido.');
            const stops=[];
            return { descriptor: [kind, args, stops], addColorStop(p,c) {
              if(stops.length>=16 || !number(p) || p<0 || p>1 || typeof c !== 'string')
                throw Error('Parada de gradiente MGT inválida.');
              stops.push([p,c]);
            }};
          };
          return (width,height,fps,frames,frame,seed,recommendedDuration,parameterJson) => {
            const list=[];
            const A=Object.freeze({width,height,naturalWidth:width,naturalHeight:height});
            const B=Object.freeze({width,height,naturalWidth:width,naturalHeight:height});
            const ops=Object.create(null);
            for(const m of methods) ops[m]=(...args)=>{
              if(args.some(v=>typeof v!=='number' && typeof v!=='boolean'))
                throw Error('Argumento de dibujo MGT inválido.');
              emit(list,m,args);
            };
            ops.drawImage=(source,...args)=>{
              if(source!==A && source!==B) throw Error('Las entradas admitidas son A y B.');
              if(![2,4,8].includes(args.length) || args.some(v=>!number(v))) throw Error('drawImage MGT tiene un número inválido de argumentos.');
              emit(list,'image',[source===A ? 0 : 1, ...args]);
            };
            ops.createLinearGradient=(...args)=>gradient(1,args);
            ops.createRadialGradient=(...args)=>gradient(2,args);
            const g=new Proxy(ops,{
              get(target,key) {
                if(!(key in target)) throw Error('Operación de Canvas fuera del perfil GPU: '+String(key));
                return target[key];
              },
              set(target,key,value) {
                if(!properties.has(key)) throw Error('Propiedad de Canvas fuera del perfil GPU: '+String(key));
                if(key.endsWith('Style') && typeof value !== 'string' && !Array.isArray(value?.descriptor))
                  throw Error('Estilo de Canvas fuera del perfil GPU.');
                if(key==='shadowColor' && typeof value!=='string') throw Error('Color de sombra inválido.');
                if(!key.endsWith('Style') && key!=='globalCompositeOperation' && key!=='shadowColor' && !number(value))
                  throw Error('Valor de Canvas fuera del perfil GPU.');
                if(key==='globalCompositeOperation' &&
                  !['source-over','lighter','screen','multiply','difference'].includes(value))
                  throw Error('Modo de mezcla fuera del perfil GPU.');
                target[key]=value;
                emit(list,key,[typeof value==='object' ? value.descriptor : value]);
                return true;
              }
            });
            const canvas={width,height,getContext(type) { if(type!=='2d') throw Error('Se requiere Canvas 2D.'); return g; }};
            const root={querySelector(selector) {
              if(selector!=='canvas' && selector!=='#mgt-canvas') throw Error('Selector fuera del perfil GPU.');
              return canvas;
            }};
            const progress=frame/(frames-1), time=frame/fps, offset=frame-Math.ceil(frames/2);
            const random=index=>{
              let n=((seed>>>0)^Math.imul((index>>>0)+1,0x9e3779b1))>>>0;
              n=Math.imul(n^(n>>>16),0x21f0aaad); n=Math.imul(n^(n>>>15),0x735a2d97);
              return ((n^(n>>>15))>>>0)/4294967296;
            };
            const result=draw(Object.freeze({root,inputA:A,inputB:B,params:Object.freeze(JSON.parse(parameterJson)),
              width,height,fps,frame,time,duration:frames/fps,progress,authoredTime:progress*recommendedDuration,
              recommendedDuration,cutOffsetFrames:offset,cutOffsetSeconds:offset/fps,seed,random}));
            if(result!==undefined && result!==null) throw Error('render debe dibujar de forma síncrona.');
            return JSON.stringify(list);
          };
        })()
        """;
}
