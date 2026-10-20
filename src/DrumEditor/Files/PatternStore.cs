using System.Text.Json;
using System.Text.Json.Nodes;
using DrumEditor.Domain;
namespace DrumEditor.Files;
public static class PatternStore
{
 public sealed record LoadResult(Pattern Pattern,bool Migrated);
 public static Pattern Load(string path)=>LoadWithMetadata(path).Pattern;
 public static LoadResult LoadWithMetadata(string path)
 {
  if(new FileInfo(path).Length>10*1024*1024)throw new InvalidDataException("10MiBを超えるファイルです。");
  string json=File.ReadAllText(path);using var document=JsonDocument.Parse(json);
  string[] required=["schemaVersion","name","bpm","ppq","beatsPerBar","bars","gridDivision","metronomeEnabled","selectedInstrument","fold","tracks","notes"];
  if(document.RootElement.ValueKind!=JsonValueKind.Object||required.Any(key=>!document.RootElement.TryGetProperty(key,out _)))throw new InvalidDataException("パターンに必要な項目がありません。");
  int version=document.RootElement.GetProperty("schemaVersion").GetInt32();
  bool migrated=version is 1 or 2 or 3 or 4;
  if(version is 1 or 2)
  {
   var root=JsonNode.Parse(json)!.AsObject();
   void Rename(JsonObject item,string key){if(item[key]?.GetValue<string>()=="Tom")item[key]="HighMiddleTom";else if(item[key]?.GetValue<string>() is "LowTom" or "LowMiddleTom" or "HighMiddleTom" or "HighTom")throw new InvalidDataException("旧版に新しいTom名は使用できません。");}
   Rename(root,"selectedInstrument");
   foreach(string key in new[]{"tracks","notes"})foreach(var item in root[key]!.AsArray())Rename(item!.AsObject(),"instrument");
   json=root.ToJsonString();
  }
  if(version>=4 && (document.RootElement.GetProperty("tracks").ValueKind!=JsonValueKind.Array || document.RootElement.GetProperty("tracks").EnumerateArray().Any(t=>t.ValueKind!=JsonValueKind.Object || !t.TryGetProperty("volumePercent",out var v) || v.ValueKind!=JsonValueKind.Number || !v.TryGetInt32(out int value) || value is <0 or >100)))throw new InvalidDataException("各楽器の音量0～100が必要です。");
  if(version==5 && (!document.RootElement.TryGetProperty("swingPercent",out var swing)||swing.ValueKind!=JsonValueKind.Number||!swing.TryGetInt32(out _)||document.RootElement.GetProperty("notes").EnumerateArray().Any(n=>!n.TryGetProperty("timingOffsetTick",out var value)||value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out _))))throw new InvalidDataException("スウィングと微調整が必要です。");
  var p=JsonSerializer.Deserialize<Pattern>(json,Pattern.JsonOptions)??throw new InvalidDataException("空のファイルです。");p.Validate(migrated);
  if(migrated){p.Tracks.AddRange(InstrumentCatalog.All.Where(d=>!p.Tracks.Any(t=>t.Instrument==d.Id)).Select(d=>new Track(d.Id,true)));if(version<4)p.Tracks=p.Tracks.Select(t=>t with { VolumePercent=100 }).ToList();p.SwingPercent=50;p.Notes=p.Notes.Select(n=>n with{TimingOffsetTick=0}).ToList();p.SchemaVersion=5;}
  p.Tracks=p.Tracks.OrderBy(t=>t.Instrument).ToList();p.Validate();return new(p,migrated);
 }
 public static void Save(string path,Pattern p) { p.Validate(); string temp=path+"."+Guid.NewGuid()+".tmp"; try { File.WriteAllText(temp,p.Fingerprint(),new System.Text.UTF8Encoding(false)); File.Move(temp,path,true); } finally { if(File.Exists(temp))File.Delete(temp); } }
}
