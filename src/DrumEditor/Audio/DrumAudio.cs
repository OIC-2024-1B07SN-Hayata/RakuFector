using DrumEditor.Domain;
namespace DrumEditor.Audio;
// RenderAdd is independent of waveOut; a shared host can supply its own continuous frame clock.
public sealed class DrumRenderSource
{
 public const int SampleRate=44100;
 sealed class Voice { public float[] Samples=[]; public int Position; public float Gain; public Instrument Instrument; public int Fade,FadeTotal; public float[] Tail=[]; public int TailPosition,TailFrames;public float TailGain; public Instrument TailInstrument; }
 readonly Voice[] voices=Enumerable.Range(0,64).Select(_=>new Voice()).ToArray();
 readonly SampleKit kit;
 public int Rate=>kit.SampleRate;
 readonly int shortFade,longFade;
 readonly float[] click;
 public DrumRenderSource(SampleKit kit)
 {
  if(kit.Samples.Length!=InstrumentCatalog.Count||kit.Samples.Any(s=>s is null||s.Stereo.Length==0||s.Stereo.Length%2!=0))throw new InvalidDataException("13音色の準備済みPCMが必要です。");
  this.kit=kit;shortFade=Math.Max(1,(int)Math.Round(Rate*.002));longFade=Math.Max(1,(int)Math.Round(Rate*.005));
  click=new float[(int)(Rate*.05)*2];for(int i=0;i<click.Length/2;i++){float v=(float)(Math.Sin(i*2*Math.PI*1500/Rate)*Math.Exp(-i/(Rate*.0068))*0.3);click[i*2]=click[i*2+1]=v;}
 }
 sealed record Snapshot(Pattern Pattern,Note[] Notes,int Start,int End,long Revision);
 Pattern active=new();Snapshot pending=new(new Pattern(),[],0,11520,0);long origin,loop=-1;int noteIndex,beatIndex;Note[] events=[];
 int rangeStart,rangeEnd=11520;
 readonly float[] trackGains=Enumerable.Repeat(1f,InstrumentCatalog.Count).ToArray();
 float[] volumeTargets=Enumerable.Repeat(1f,InstrumentCatalog.Count).ToArray();
 public void SetTrackVolumes(Pattern p){var targets=new float[InstrumentCatalog.Count];foreach(var t in p.Tracks)targets[(int)t.Instrument]=t.VolumePercent/100f;Volatile.Write(ref volumeTargets,targets);}
 public int EnabledMask=InstrumentCatalog.EnabledMask,SoloMask;public volatile bool Metronome;
 public long PositionFrames;
 long revision; public long AppliedRevision;
 public long Publish(Pattern p,int startTick=0,int? endTick=null){var copy=p.Copy();int end=endTick??p.Length;if(startTick<0||end>p.Length||startTick>=end||startTick%(p.BeatsPerBar*960)!=0||end%(p.BeatsPerBar*960)!=0)throw new InvalidDataException("ループ範囲が不正です。");Timing.ValidateRange(copy,startTick,end);SetTrackVolumes(p);long version=Interlocked.Increment(ref revision);Volatile.Write(ref pending,new Snapshot(copy,copy.Notes.Where(n=>n.Tick>=startTick&&n.Tick<end).Select(n=>n with{Tick=Timing.EffectiveTick(copy,n),TimingOffsetTick=0}).OrderBy(n=>n.Tick).ThenBy(n=>n.Instrument).ToArray(),startTick,end,version));return version;}
 public void Start(long frame){origin=frame;loop=-1;PositionFrames=0;var targets=Volatile.Read(ref volumeTargets);for(int i=0;i<trackGains.Length;i++)trackGains[i]=targets[i];foreach(var v in voices){v.Position=v.Samples.Length;v.TailFrames=0;}}
 long EventFrame(long loopIndex,int tick)=>origin+(long)Math.Round((loopIndex*(rangeEnd-rangeStart)+(double)tick-rangeStart)*60*Rate/(active.Bpm*960d),MidpointRounding.AwayFromZero);
 public void Preview(Instrument instrument)=>Trigger(instrument,100);
 void Trigger(Instrument inst,int velocity)
 {
  if(inst==Instrument.HiHat)foreach(var v in voices)if(v.Instrument==Instrument.OpenHH&&v.Position<v.Samples.Length/2)v.Fade=v.FadeTotal=shortFade;
  Voice voice=voices[0];double lowest=double.MaxValue;foreach(var candidate in voices){if(candidate.Position>=candidate.Samples.Length/2){voice=candidate;break;}int index=candidate.Position*2;double level=candidate.Gain*Math.Max(Math.Abs(candidate.Samples[index]),Math.Abs(candidate.Samples[index+1]));if(level<lowest){lowest=level;voice=candidate;}}
  if(voice.Position<voice.Samples.Length/2){voice.Tail=voice.Samples;voice.TailPosition=voice.Position;voice.TailGain=voice.Gain;voice.TailInstrument=voice.Instrument;voice.TailFrames=shortFade;}
  var sample=kit.Samples[(int)inst];voice.Samples=sample.Stereo;voice.Position=0;voice.Gain=velocity/127f*0.5f*sample.Gain;voice.Instrument=inst;voice.Fade=0;
 }
 public void RenderAdd(float[] output,int frames,long start,bool playing)
 {
  for(int f=0;f<frames;f++)
  {
   long frame=start+f;
   var targets=Volatile.Read(ref volumeTargets);for(int i=0;i<trackGains.Length;i++){float difference=targets[i]-trackGains[i];trackGains[i]+=Math.Clamp(difference,-1f/(Rate*.02f),1f/(Rate*.02f));}
   if(playing){if(loop<0||frame>=EventFrame(loop+1,rangeStart)){loop++;var snapshot=Volatile.Read(ref pending);active=snapshot.Pattern;events=snapshot.Notes;Volatile.Write(ref AppliedRevision,snapshot.Revision);rangeStart=snapshot.Start;rangeEnd=snapshot.End;noteIndex=0;beatIndex=rangeStart/960;}
    int mask=Volatile.Read(ref EnabledMask),solo=Volatile.Read(ref SoloMask);if(solo!=0)mask&=solo;
    while(noteIndex<events.Length&&EventFrame(loop,events[noteIndex].Tick)<=frame){var n=events[noteIndex++];bool closed=false;if(n.Instrument==Instrument.OpenHH&&(mask&4)!=0){for(int j=noteIndex-2;j>=0&&events[j].Tick==n.Tick;j--)if(events[j].Instrument==Instrument.HiHat){closed=true;break;}}if(!closed&&(mask&(1<<(int)n.Instrument))!=0)Trigger(n.Instrument,n.Velocity);}
    while(beatIndex<rangeEnd/960&&EventFrame(loop,beatIndex*960)<=frame){if(Metronome){Voice? v=null;foreach(var candidate in voices)if(candidate.Position>=candidate.Samples.Length/2){v=candidate;break;}if(v!=null){v.Samples=click;v.Position=0;v.Gain=beatIndex%active.BeatsPerBar==0?1:0.55f;v.Instrument=(Instrument)InstrumentCatalog.Count;v.Fade=0;}}beatIndex++;}
    PositionFrames=frame-EventFrame(loop,rangeStart);
    foreach(var v in voices)if((int)v.Instrument<InstrumentCatalog.Count&&(mask&(1<<(int)v.Instrument))==0&&v.Position<v.Samples.Length/2&&v.Fade==0)v.Fade=v.FadeTotal=longFade;
   }
   float left=0,right=0;foreach(var v in voices){if(v.TailFrames>0&&v.TailPosition<v.Tail.Length/2){float tailGain=v.TailGain*v.TailFrames/(float)shortFade*((int)v.TailInstrument<InstrumentCatalog.Count?trackGains[(int)v.TailInstrument]:1);left+=v.Tail[v.TailPosition*2]*tailGain;right+=v.Tail[v.TailPosition*2+1]*tailGain;v.TailPosition++;v.TailFrames--;}if(v.Position>=v.Samples.Length/2)continue;float gain=v.Gain*((int)v.Instrument<InstrumentCatalog.Count?trackGains[(int)v.Instrument]:1);if(v.Fade>0){gain*=v.Fade/(float)v.FadeTotal;if(--v.Fade==0){v.Position=v.Samples.Length;continue;}}left+=v.Samples[v.Position*2]*gain;right+=v.Samples[v.Position*2+1]*gain;v.Position++;}output[f*2]+=left;output[f*2+1]+=right;
  }
 }
 public void FadeStop(){foreach(var v in voices){if(v.Position<v.Samples.Length/2)v.Fade=v.FadeTotal=longFade;v.TailFrames=0;}PositionFrames=0;}
}
