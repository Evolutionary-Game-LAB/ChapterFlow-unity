using System;
using System.IO;
using System.Linq;
using ChapterFlow;
using ChapterFlow.Tests;
using Newtonsoft.Json.Linq;
class Program {
 static JToken Normal(JToken t) {
  if(t is JObject o)return new JObject(o.Properties().Select(p=>new JProperty(p.Name,Normal(p.Value))));
  if(t is JArray ar)return new JArray(ar.Select(Normal));
  if(t.Type==JTokenType.Integer||t.Type==JTokenType.Float)return new JValue((decimal)t);
  return t.DeepClone();
 }
 static void Equal(JToken a,JToken b,string label) { if(!JToken.DeepEquals(Normal(a),Normal(b)))throw new Exception(label+": expected "+b+", got "+a); }
 static int Main(string[] args) {
  try {
   int steps=0,saves=0,checks=RuntimeChecks.Run(File.ReadAllText(args[1]));
   var cases=JArray.Parse(File.ReadAllText(args[0]));
   foreach(var item in cases) {
    var chapter=ChapterDefinition.Parse(item["chapter"].ToString());var session=new ChapterSession(chapter);
    foreach(var expected in item["steps"]) {
     var cmd=expected["command"];string op=(string)cmd["op"],id=(string)cmd["id"];
     var result=op=="start"?session.RequestEventStart(id):op=="end"?session.RequestEventEnd(id):session.ReceiveGameplaySignal(id,(string)cmd["key"]);
     string label=(string)item["name"]+" #"+steps;
     Equal(new JValue(result.Success),expected["ok"],label+" success");
     var state=new JObject(session.State.Select(p=>new JProperty(p.Key,p.Value.ToToken())));
     Equal(state,expected["state"],label+" state");
     Equal(JToken.FromObject(session.CompletedEvents),expected["completed"],label+" history");
     Equal(session.ActiveEventId==null?JValue.CreateNull():new JValue(session.ActiveEventId),expected["active"],label+" active");
     Equal(session.LastCompletedEventId==null?JValue.CreateNull():new JValue(session.LastCompletedEventId),expected["last"],label+" last");
     Equal(session.EndingId==null?JValue.CreateNull():new JValue(session.EndingId),expected["ending"],label+" ending");
     Equal(new JArray(session.GetAvailableEvents().Select(e=>e.Id).OrderBy(v=>v,StringComparer.Ordinal)),expected["available"],label+" availability");
     var save=session.SaveJson();var restored=new ChapterSession(chapter);
     var load=restored.LoadJson(save);if(!load.Success)throw new Exception(label+" restore "+load.Error);
     Equal(JObject.Parse(restored.SaveJson()),JObject.Parse(save),label+" save");
     session=restored;steps++;saves++;
    }
   }
   Console.WriteLine(new JObject{["passed"]=true,["cases"]=cases.Count,["commands"]=steps,["saveRestores"]=saves,["legacyChecks"]=checks}.ToString());return 0;
  }catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
 }
}
