using System;
using System.Collections.Generic;
using System.Linq;

namespace Prometheus {
 public sealed class DuckingRule01016 {
  public string target_track_id,voice_track_id;
  public double amount_db=-12,threshold_db=-32,attack_ms=80,release_ms=400;
  public bool enabled=true;
 }
 public static class Ducking01016 {
  public const string Namespace="prometheus.audio.ducking";
  static Extension Data(Sequence sequence){return sequence.extensions.FirstOrDefault(e=>e!=null&&e.@namespace==Namespace&&e.version=="1");}
  public static List<DuckingRule01016> All(Sequence sequence){var data=Data(sequence);object raw;if(data==null||data.payload==null||!data.payload.TryGetValue("rules_json",out raw)||raw==null)return new List<DuckingRule01016>();return Store.Json().Deserialize<List<DuckingRule01016>>(Convert.ToString(raw))??new List<DuckingRule01016>();}
  public static DuckingRule01016 For(Sequence sequence,string targetId){return All(sequence).FirstOrDefault(r=>r.target_track_id==targetId);}
  static void Write(Sequence sequence,List<DuckingRule01016> rules){var data=Data(sequence);if(data==null){data=new Extension{@namespace=Namespace,version="1",payload=new Dictionary<string,object>()};sequence.extensions.Add(data);}data.payload["rules_json"]=Store.Json().Serialize(rules);}
  public static void Set(Sequence sequence,DuckingRule01016 rule){var rules=All(sequence);rules.RemoveAll(r=>r.target_track_id==rule.target_track_id);rules.Add(rule);ValidateRules(sequence,rules);Write(sequence,rules);}
  public static void Remove(Sequence sequence,string targetId){var rules=All(sequence);rules.RemoveAll(r=>r.target_track_id==targetId);Write(sequence,rules);}
  public static void Validate(Sequence sequence){ValidateRules(sequence,All(sequence));}
  static void ValidateRules(Sequence sequence,List<DuckingRule01016> rules){var targets=new HashSet<string>();foreach(var rule in rules){if(rule==null||!targets.Add(rule.target_track_id)||rule.target_track_id==rule.voice_track_id||!sequence.tracks.Any(t=>t.id==rule.target_track_id&&t.kind=="audio")||!sequence.tracks.Any(t=>t.id==rule.voice_track_id&&t.kind=="audio")||rule.amount_db> -1||rule.amount_db< -30||rule.threshold_db> -6||rule.threshold_db< -60||rule.attack_ms<5||rule.attack_ms>1000||rule.release_ms<50||rule.release_ms>3000)throw new Exception("Configuración de Auto-Ducking inválida");}}
 }
}
