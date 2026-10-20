namespace DrumEditor.Domain;

public sealed record InstrumentDefinition(Instrument Id, string Name, int MidiNote, string FileName, double RandomProbability);
public static class InstrumentCatalog
{
 public const int Count=13, EnabledMask=(1<<Count)-1, MaxNotes=Count*64*6*4;
 public static readonly InstrumentDefinition[] All =
 [
  new(Instrument.Kick,"Kick",36,"bass_drum.wav",.25), new(Instrument.Snare,"Snare",38,"snare.wav",.20),
  new(Instrument.HiHat,"HiHat",42,"closed_hihat.wav",.50), new(Instrument.OpenHH,"OpenHH",46,"open_hihat.wav",.10),
  new(Instrument.Clap,"Clap",39,"clap.wav",.15), new(Instrument.Perc,"Perc",75,"claves.wav",.15),
  new(Instrument.LowTom,"LowTom",45,"lowtom.wav",.04), new(Instrument.LowMiddleTom,"LowMiddleTom",47,"lowmiddletom.wav",.04),
  new(Instrument.HighMiddleTom,"HighMiddleTom",48,"highmiddletom.wav",.04), new(Instrument.HighTom,"HighTom",50,"hightom.wav",.04),
  new(Instrument.FloorTom,"FloorTom",41,"floor_tom.wav",.06),
  new(Instrument.Crash,"Crash",49,"crash.wav",.04), new(Instrument.Ride,"Ride",51,"ride.wav",.25)
 ];
 public static InstrumentDefinition Get(Instrument id)=>All[(int)id];
 public static Pattern CreateDemo()
 {
  var p=new Pattern{Name="13 Tracks Demo",Bpm=120};
  int[][] steps=[Enumerable.Range(0,12).Select(x=>x*4).ToArray(),[4,12,20,28,36,44],Enumerable.Range(0,24).Select(x=>x*2).ToArray(),[7,15,23,31,39,47],[4,12,20,28,36,44],[3,11,19,27,35,43],[14,30,46],[13,29,45],[12,28,44],[11,27,43],[15,31,47],[0,16,32],Enumerable.Range(0,12).Select(x=>x*4).ToArray()];
  foreach(var d in All)foreach(int step in steps[(int)d.Id])p.Notes.Add(new(Guid.NewGuid(),d.Id,step*240,100));
  return p;
 }
}
