using System.Text.Json;
namespace DrumEditor.Domain;
public enum Instrument { Kick, Snare, HiHat, OpenHH, Clap, Perc, LowTom, LowMiddleTom, HighMiddleTom, HighTom, FloorTom, Crash, Ride }
public sealed record Note(Guid Id, Instrument Instrument, int Tick, int Velocity, int TimingOffsetTick = 0);
public sealed record Track(Instrument Instrument, bool Enabled, int VolumePercent = 100);
public sealed class Pattern
{
 public int SchemaVersion { get; set; } = 5;
 public string Name { get; set; } = "Untitled";
 public int SwingPercent { get; set; } = 50;
 public int Bpm { get; set; } = 40;
 public int Ppq { get; set; } = 960;
 public int BeatsPerBar { get; set; } = 4;
 public int Bars { get; set; } = 3;
 public int GridDivision { get; set; } = 16;
 public bool MetronomeEnabled { get; set; }
 public Instrument SelectedInstrument { get; set; }
 public bool Fold { get; set; }
 public List<Track> Tracks { get; set; } = Enum.GetValues<Instrument>().Select(i => new Track(i, true)).ToList();
 public List<Note> Notes { get; set; } = [];
 [System.Text.Json.Serialization.JsonIgnore] public int Length => Bars * BeatsPerBar * Ppq;
 public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(null, false) } };
 public Pattern Copy() => JsonSerializer.Deserialize<Pattern>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
 public string Fingerprint() => JsonSerializer.Serialize(this, JsonOptions);
 public string EditFingerprint() { var copy=Copy(); copy.SelectedInstrument=Instrument.Kick; copy.Fold=false; copy.GridDivision=16; return copy.Fingerprint(); }
 public void Validate(bool legacy=false)
 {
  var allowed=legacy ? (SchemaVersion==1 ? InstrumentCatalog.All.Take(6) : SchemaVersion==2 ? InstrumentCatalog.All.Where(d=>d.Id is not (Instrument.LowTom or Instrument.LowMiddleTom or Instrument.HighTom)) : InstrumentCatalog.All).Select(d=>d.Id).ToHashSet() : Enum.GetValues<Instrument>().ToHashSet();
  int trackCount=allowed.Count;
  if((legacy ? SchemaVersion is not (1 or 2 or 3 or 4) : SchemaVersion!=5) || SwingPercent is <50 or >75 || Ppq != 960 || Bpm is < 1 or > 300 || BeatsPerBar is < 3 or > 6 || Bars is < 1 or > 64 || GridDivision is not (4 or 8 or 16) || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || !allowed.Contains(SelectedInstrument)) throw new InvalidDataException("パターン設定が不正です。");
  if(Tracks is null || Tracks.Count != trackCount || Tracks.Any(t=>t is null || t.VolumePercent is <0 or >100 || !allowed.Contains(t.Instrument)) || Tracks.Select(t=>t.Instrument).Distinct().Count()!=trackCount) throw new InvalidDataException($"{trackCount}種類の楽器設定が必要です。");
  if(Notes is null || Notes.Count>trackCount*64*6*4 || Notes.Any(n=> n is null || n.Id==Guid.Empty || !allowed.Contains(n.Instrument) || n.Tick<0 || n.Tick>=Length || n.Tick%240!=0 || n.Velocity is <1 or >127 || n.TimingOffsetTick is <-120 or >120 || Timing.EffectiveTick(this,n)<0 || Timing.EffectiveTick(this,n)>=Length) || Notes.Select(n=>n.Id).Distinct().Count()!=Notes.Count || Notes.Select(n=>(n.Instrument,n.Tick)).Distinct().Count()!=Notes.Count) throw new InvalidDataException("音符の位置、強さ、ID、件数、または重複が不正です。");
  if(Notes.Select(n=>(n.Instrument,Tick:Timing.EffectiveTick(this,n))).Distinct().Count()!=Notes.Count)throw new InvalidDataException("実効発音位置が重なっています。");
 }
}
public sealed class Editor
{
 public Pattern Pattern { get; private set; } = new();
 readonly List<Pattern> undo = [], redo = [];
 public bool CanUndo => undo.Count>0;
 public bool CanRedo => redo.Count>0;
 public bool UndoChangesTiming => CanUndo && TimingDiffers(undo[^1]);
 public bool RedoChangesTiming => CanRedo && TimingDiffers(redo[^1]);
 bool TimingDiffers(Pattern other)=>other.SwingPercent!=Pattern.SwingPercent||other.Bpm!=Pattern.Bpm||other.BeatsPerBar!=Pattern.BeatsPerBar||other.Bars!=Pattern.Bars;
 public void Change(Action<Pattern> action) { var old=Pattern.Copy(); try { action(Pattern); Pattern.Validate(); } catch { Pattern=old; throw; } if(old.EditFingerprint()==Pattern.EditFingerprint()) return; undo.Add(old); if(undo.Count>100) undo.RemoveAt(0); redo.Clear(); }
 public void Undo() { if(!CanUndo)return; redo.Add(Pattern.Copy()); Pattern=RestoreView(undo[^1]); undo.RemoveAt(undo.Count-1); }
 public void Redo() { if(!CanRedo)return; undo.Add(Pattern.Copy()); Pattern=RestoreView(redo[^1]); redo.RemoveAt(redo.Count-1); }
 Pattern RestoreView(Pattern target) { var copy=target.Copy(); copy.SelectedInstrument=Pattern.SelectedInstrument; copy.Fold=Pattern.Fold; copy.GridDivision=Pattern.GridDivision; return copy; }
 public void Load(Pattern p) { p.Validate(); Pattern=p.Copy(); undo.Clear(); redo.Clear(); }
 public static int RemapTick(int tick,int oldMeter,int newMeter) => tick/(oldMeter*960)*(newMeter*960)+tick%(oldMeter*960);
}
