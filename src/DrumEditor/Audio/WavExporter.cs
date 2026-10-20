using DrumEditor.Domain;
namespace DrumEditor.Audio;
public sealed record WavOptions(int First,int Last,int Repeats,bool Tail,bool Metronome,int SoloMask,float Volume);
public static class WavExporter
{
    public static long Frames(Pattern p,SampleKit kit,WavOptions o)
    {
        p.Validate();EditingOperations.ValidateBars(p,o.First,o.Last);if(o.Repeats is <1 or >16 || !float.IsFinite(o.Volume)||o.Volume is <0 or >1)throw new InvalidDataException("出力設定が不正です。");
        int start=(o.First-1)*p.BeatsPerBar*960,end=o.Last*p.BeatsPerBar*960;Timing.ValidateRange(p,start,end);
        double rate=60d*kit.SampleRate/(p.Bpm*960);long frames=(long)Math.Round((end-start)*o.Repeats*rate,MidpointRounding.AwayFromZero);
        if(o.Tail)
        {
            int mask=p.Tracks.Where(t=>t.Enabled).Sum(t=>1<<(int)t.Instrument);if(o.SoloMask!=0)mask&=o.SoloMask;
            long final=frames;
            foreach(var n in p.Notes.Where(n=>n.Tick>=start&&n.Tick<end&&(mask&(1<<(int)n.Instrument))!=0))final=Math.Max(final,(long)Math.Round(((end-start)*(o.Repeats-1)+Timing.EffectiveTick(p,n)-start)*rate,MidpointRounding.AwayFromZero)+kit.Samples[(int)n.Instrument].Frames);
            if(o.Metronome)final=Math.Max(final,(long)Math.Round(((end-start)*o.Repeats-960)*rate)+kit.SampleRate/20);
            frames=final;
        }
        if(frames>kit.SampleRate*1800L||frames*4>1024L*1024*1024)throw new InvalidDataException("WAVは30分または1GiB以内にしてください。");return frames;
    }
    public static void Save(string path,Pattern p,SampleKit kit,WavOptions o,CancellationToken cancellation,IProgress<double>? progress=null)
    {
        long total=Frames(p,kit,o);int start=(o.First-1)*p.BeatsPerBar*960,end=o.Last*p.BeatsPerBar*960;
        long musical=(long)Math.Round((end-start)*o.Repeats*60d*kit.SampleRate/(p.Bpm*960),MidpointRounding.AwayFromZero);
        var source=new DrumRenderSource(kit){EnabledMask=p.Tracks.Where(t=>t.Enabled).Sum(t=>1<<(int)t.Instrument),SoloMask=o.SoloMask,Metronome=o.Metronome};source.Publish(p,start,end);source.Start(0);
        var stage=new DeviceOutputStage(kit.SampleRate,o.Volume);var data=new float[2048*2];string temp=path+"."+Guid.NewGuid()+".tmp";
        long lastProgress=0;
        try
        {
            using(var file=new BinaryWriter(File.Create(temp)))
            {
                file.Write("RIFF"u8.ToArray());file.Write((int)(36+total*4));file.Write("WAVEfmt "u8.ToArray());file.Write(16);file.Write((ushort)1);file.Write((ushort)2);file.Write(kit.SampleRate);file.Write(kit.SampleRate*4);file.Write((ushort)4);file.Write((ushort)16);file.Write("data"u8.ToArray());file.Write((int)(total*4));
                for(long position=0;position<total;)
                {
                    cancellation.ThrowIfCancellationRequested();int count=(int)Math.Min(2048,total-position);bool playing=position<musical;if(playing)count=(int)Math.Min(count,musical-position);
                    Array.Clear(data);source.RenderAdd(data,count,position,playing);stage.Process(data,count,o.Volume);
                    for(int i=0;i<count;i++){float fade=!o.Tail&&position+i>total-kit.SampleRate*.005?(float)((total-position-i-1)/(kit.SampleRate*.005)):1;file.Write((short)(Math.Clamp(data[i*2]*fade,-1,1)*32767));file.Write((short)(Math.Clamp(data[i*2+1]*fade,-1,1)*32767));}
                    position+=count;long now=Environment.TickCount64;if(position==total||now-lastProgress>=50){progress?.Report(position/(double)total);lastProgress=now;}
                }
            }
            cancellation.ThrowIfCancellationRequested();File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
