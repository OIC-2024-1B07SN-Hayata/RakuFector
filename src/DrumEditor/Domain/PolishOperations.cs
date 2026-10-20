namespace DrumEditor.Domain;

public static class Timing
{
    public static int EffectiveTick(Pattern p,Note n)
    {
        int r=n.Tick%960;double s=p.SwingPercent/100d;
        return n.Tick-r+(int)Math.Round(r<=480?2*s*r:960*s+2*(1-s)*(r-480),MidpointRounding.AwayFromZero)+n.TimingOffsetTick;
    }
    public static void ValidateRange(Pattern p,int start,int end)
    {
        if(p.Notes.Any(n=>n.Tick>=start&&n.Tick<end&&(EffectiveTick(p,n)<start||EffectiveTick(p,n)>=end)))throw new InvalidDataException("選択範囲の外へ発音する音符があります。微調整を確認してください。");
    }
}
public static class PolishOperations
{
    public static HashSet<Instrument> ResolveInstruments(int mode,Instrument selected,IReadOnlySet<Instrument> checkedInstruments)
    {
        return mode switch
        {
            0=>InstrumentCatalog.All.Select(d=>d.Id).ToHashSet(),
            1=>[selected],
            2=>InstrumentCatalog.All.Where(d=>checkedInstruments.Contains(d.Id)).Select(d=>d.Id).ToHashSet(),
            3=>InstrumentCatalog.All.Where(d=>!checkedInstruments.Contains(d.Id)).Select(d=>d.Id).ToHashSet(),
            _=>throw new InvalidDataException("対象楽器の指定が不正です。")
        };
    }
    public static void EditNote(Pattern p,Guid id,int bar,int beat,int subTick,int velocity,int offset)
    {
        if(bar<1||bar>p.Bars||beat<1||beat>p.BeatsPerBar||subTick is <0 or >959)throw new InvalidDataException("音符位置が範囲外です。");
        int index=p.Notes.FindIndex(n=>n.Id==id);if(index<0)throw new InvalidDataException("音符を選択してください。");
        p.Notes[index]=p.Notes[index] with{Tick=((bar-1)*p.BeatsPerBar+beat-1)*960+subTick,Velocity=velocity,TimingOffsetTick=offset};p.Validate();
    }
    public static void Target(Pattern p,IReadOnlySet<Instrument> instruments,int first,int last,bool random,int seed)
    {
        EditingOperations.ValidateBars(p,first,last);if(instruments.Count==0)throw new InvalidDataException("楽器を選択してください。");
        int start=(first-1)*p.BeatsPerBar*960,end=last*p.BeatsPerBar*960;
        p.Notes.RemoveAll(n=>instruments.Contains(n.Instrument)&&n.Tick>=start&&n.Tick<end);
        if(!random)return;
        var kept=p.Notes.Select(n=>(n.Instrument,n.Tick)).ToHashSet();var rng=new Random(seed);
        for(int tick=start;tick<end;tick+=3840/p.GridDivision)
        {
            bool closed=kept.Contains((Instrument.HiHat,tick)),open=kept.Contains((Instrument.OpenHH,tick));
            foreach(var d in InstrumentCatalog.All.Where(d=>instruments.Contains(d.Id)))
            {
                if(rng.NextDouble()>=d.RandomProbability||d.Id==Instrument.HiHat&&open||d.Id==Instrument.OpenHH&&closed)continue;
                p.Notes.Add(new(Guid.NewGuid(),d.Id,tick,rng.Next(80,111)));if(d.Id==Instrument.HiHat)closed=true;if(d.Id==Instrument.OpenHH)open=true;
            }
        }
    }
}
public sealed record RhythmPreset(string Id,string Name,int Bpm,Pattern Pattern);
public static class RhythmPresets
{
    public static RhythmPreset[] All { get; }=Create();
    static RhythmPreset[] Create()
    {
        var names=new[]{"8ビート","16ビート","ハーフタイム","4つ打ち","タムフィル"};var result=new List<RhythmPreset>();
        for(int i=0;i<5;i++)
        {
            var p=new Pattern{Bars=1,Bpm=120,Name=names[i]};
            void Add(Instrument instrument,IEnumerable<int> steps){foreach(int step in steps)p.Notes.Add(new(Guid.NewGuid(),instrument,step*240,100));}
            if(i==4){Add(Instrument.HighTom,[0,2,4,6]);Add(Instrument.HighMiddleTom,[8,10]);Add(Instrument.LowTom,[12,14]);}
            else {Add(Instrument.Kick,i==3?[0,4,8,12]:[0,8]);Add(Instrument.Snare,i==2?[8]:[4,12]);Add(Instrument.HiHat,Enumerable.Range(0,i==1?16:8).Select(x=>x*(i==1?1:2)));}
            p.Validate();result.Add(new("builtin-"+(i+1),names[i],120,p));
        }
        return result.ToArray();
    }
    public static void Apply(Pattern p,RhythmPreset preset,int? first,int? last,bool tempo)
    {
        var original=preset.Pattern;original.Validate();
        if(first.HasValue){EditingOperations.ValidateBars(p,first.Value,last!.Value);if(p.BeatsPerBar!=original.BeatsPerBar)throw new InvalidDataException("拍子が異なります。");var clip=EditingOperations.CopyBars(original,1,1);for(int bar=first.Value;bar<=last.Value;bar++)EditingOperations.PasteBars(p,clip,bar);}
        else{p.BeatsPerBar=original.BeatsPerBar;p.Bars=original.Bars;p.Notes=original.Notes.Select(n=>n with{Id=Guid.NewGuid()}).ToList();}
        if(tempo)p.Bpm=preset.Bpm;
    }
}
