using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace Prometheus {
 public sealed class ShortcutCommand {
  public readonly string Id, Name;
  public readonly Keys Default;
  public ShortcutCommand(string id,string name,Keys key){Id=id;Name=name;Default=key;}
 }
 public sealed class ShortcutMap {
  public static readonly ShortcutCommand[] Commands={
   new ShortcutCommand("tool.select","Seleccionar",Keys.V),
   new ShortcutCommand("tool.razor","Tijeras",Keys.C),
   new ShortcutCommand("tool.text","Texto en monitor",Keys.T),
   new ShortcutCommand("tool.mask","Última herramienta de máscara",Keys.Control|Keys.Shift|Keys.M),
   new ShortcutCommand("tool.zoom","Zoom",Keys.Z),
   new ShortcutCommand("tool.hand","Mano",Keys.H),
   new ShortcutCommand("tool.slip","Mover contenido",Keys.None),
   new ShortcutCommand("tool.forward","Seleccionar hacia adelante",Keys.A),
   new ShortcutCommand("tool.backward","Seleccionar hacia atrás",Keys.Shift|Keys.A),
   new ShortcutCommand("tool.up","Seleccionar pistas superiores",Keys.None),
   new ShortcutCommand("tool.down","Seleccionar pistas inferiores",Keys.None),
   new ShortcutCommand("tool.all","Seleccionar toda la línea de tiempo",Keys.None),
   new ShortcutCommand("edit.divide","Dividir",Keys.Control|Keys.K),
   new ShortcutCommand("edit.undo","Deshacer",Keys.Control|Keys.Z),
   new ShortcutCommand("edit.redo","Rehacer",Keys.Control|Keys.Y),
   new ShortcutCommand("edit.redo.alias","Rehacer (atajo alternativo)",Keys.Control|Keys.Shift|Keys.Z),
   new ShortcutCommand("edit.copy","Copiar",Keys.Control|Keys.C),
   new ShortcutCommand("edit.cut","Cortar selección",Keys.Control|Keys.X),
   new ShortcutCommand("edit.paste","Pegar",Keys.Control|Keys.V),
   new ShortcutCommand("edit.delete","Eliminar",Keys.Delete),
   new ShortcutCommand("edit.ripple-delete","Eliminar y cerrar espacio",Keys.Shift|Keys.Delete),
   new ShortcutCommand("edit.paste-attributes","Pegar atributos",Keys.Control|Keys.Alt|Keys.V),
   new ShortcutCommand("timeline.snap","Alternar ajuste magnético",Keys.S),
   new ShortcutCommand("project.save","Guardar proyecto",Keys.Control|Keys.S),
   new ShortcutCommand("project.open","Abrir proyecto",Keys.Control|Keys.O),
   new ShortcutCommand("shelf.capture","Capturar texto para Bandeja (global)",Keys.Control|Keys.Alt|Keys.X),
   new ShortcutCommand("transport.play","Reproducir / pausar",Keys.Space)
   ,new ShortcutCommand("preview.render","Procesar tramos rojos",Keys.Enter)
   ,new ShortcutCommand("marker.add","Añadir marcador",Keys.M)
   ,new ShortcutCommand("export.mark-in","Marcar entrada",Keys.I)
   ,new ShortcutCommand("export.mark-out","Marcar salida",Keys.O)
   ,new ShortcutCommand("export.clear-marks","Borrar entrada y salida",Keys.Control|Keys.Shift|Keys.X)
   ,new ShortcutCommand("nav.marker.previous","Marcador anterior",Keys.Control|Keys.Left)
   ,new ShortcutCommand("nav.marker.next","Marcador siguiente",Keys.Control|Keys.Right)
   ,new ShortcutCommand("nav.frame.previous","Fotograma anterior",Keys.Left)
   ,new ShortcutCommand("nav.frame.next","Fotograma siguiente",Keys.Right)
   ,new ShortcutCommand("nav.five.previous","Cinco fotogramas atrás",Keys.Shift|Keys.Left)
   ,new ShortcutCommand("nav.five.next","Cinco fotogramas adelante",Keys.Shift|Keys.Right)
   ,new ShortcutCommand("nav.cut.next","Siguiente límite de clip",Keys.Up)
   ,new ShortcutCommand("nav.cut.previous","Límite de clip anterior",Keys.Down)
  };
  readonly Dictionary<string,Keys> keys=new Dictionary<string,Keys>();
  readonly string path;
  public Keys WheelZoomModifiers{get;private set;}
  public Keys WheelTrackModifiers{get{return WheelZoomModifiers==Keys.Control?Keys.Alt:Keys.Control;}}
  const string WheelZoomKey="timeline.zoom-wheel-modifiers";
  const string WheelZoomVersionKey="timeline.zoom-wheel-default-version";
  public static readonly Keys ModifierMask=Keys.Control|Keys.Alt|Keys.Shift;
  public ShortcutMap(string file){path=file;ResetAll();Load();}
  public Keys Get(string id){return keys[id];}
  public string CommandAt(Keys key){if(key==Keys.None)return null;return Commands.Where(c=>keys[c.Id]==key).Select(c=>c.Id).FirstOrDefault();}
  public ShortcutCommand Command(string id){return Commands.First(c=>c.Id==id);}
  public static bool Valid(Keys key){Keys code=key&Keys.KeyCode;return code!=Keys.None&&code!=Keys.ControlKey&&code!=Keys.ShiftKey&&code!=Keys.Menu&&code!=Keys.LWin&&code!=Keys.RWin&&code!=Keys.Escape;}
  public string Conflict(string id,Keys key){string other=CommandAt(key);return other==id?null:other;}
  public void Assign(string id,Keys key,bool replace){if(!keys.ContainsKey(id))throw new ArgumentException("Comando desconocido");if(key!=Keys.None&&!Valid(key))throw new ArgumentException("Pulsa una tecla de acción junto con los modificadores");string other=Conflict(id,key);if(other!=null){if(!replace)throw new InvalidOperationException("Atajo en uso por "+Command(other).Name);keys[other]=Keys.None;}keys[id]=key;Save();}
  public void Reset(string id){Assign(id,Command(id).Default,true);}
  public void ResetAll(){keys.Clear();foreach(var c in Commands)keys[c.Id]=c.Default;WheelZoomModifiers=Keys.Alt;}
  public void AssignWheelZoom(Keys modifiers){if((modifiers&~ModifierMask)!=0)throw new ArgumentException("Solo se admiten Ctrl, Alt y Mayús");WheelZoomModifiers=modifiers;Save();}
  public static string WheelLabel(Keys modifiers){return Label(modifiers|Keys.F24).Replace("+F24","+rueda");}
  public static string Label(Keys key){if(key==Keys.None)return "Sin asignar";var parts=new List<string>();if((key&Keys.Control)!=0)parts.Add("Ctrl");if((key&Keys.Alt)!=0)parts.Add("Alt");if((key&Keys.Shift)!=0)parts.Add("Mayús");Keys code=key&Keys.KeyCode;parts.Add(code==Keys.Space?"Espacio":code==Keys.Delete?"Supr":code.ToString());return String.Join("+",parts);}
  public void Save(){if(path==null)return;Directory.CreateDirectory(Path.GetDirectoryName(path));var data=keys.ToDictionary(x=>x.Key,x=>(int)x.Value);data[WheelZoomKey]=(int)WheelZoomModifiers;data[WheelZoomVersionKey]=2;string temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(data));if(File.Exists(path)){string backup=path+".bak";File.Replace(temp,path,backup,true);}else File.Move(temp,path);}
  void Load(){if(path==null||!File.Exists(path))return;try{var data=new JavaScriptSerializer().Deserialize<Dictionary<string,int>>(File.ReadAllText(path));var loaded=new Dictionary<string,Keys>();var occupied=new HashSet<Keys>();foreach(var c in Commands){int raw;Keys key=data.TryGetValue(c.Id,out raw)?(Keys)raw:c.Default;if(key!=Keys.None&&!Valid(key))key=Keys.None;if(key!=Keys.None&&!occupied.Add(key))key=Keys.None;loaded[c.Id]=key;}foreach(var pair in loaded)keys[pair.Key]=pair.Value;int wheel;if(data.TryGetValue(WheelZoomKey,out wheel)){Keys value=(Keys)wheel;if((value&~ModifierMask)==0){int version;WheelZoomModifiers=!data.TryGetValue(WheelZoomVersionKey,out version)&&value==(Keys.Control|Keys.Alt)?Keys.Alt:value;}}}catch{ResetAll();}}
 }
}
