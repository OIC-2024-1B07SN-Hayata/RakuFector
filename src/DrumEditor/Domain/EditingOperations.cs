namespace DrumEditor.Domain;

public sealed record BarClipboard(int Bars, int Meter, Note[] Notes);
public static class EditingOperations
{
    public static BarClipboard CopyBars(Pattern p, int first, int last)
    {
        ValidateBars(p,first,last);
        int start=(first-1)*p.BeatsPerBar*960,end=last*p.BeatsPerBar*960;
        return new(last-first+1,p.BeatsPerBar,p.Notes.Where(n=>n.Tick>=start&&n.Tick<end).Select(n=>n with { Tick=n.Tick-start }).ToArray());
    }
    public static int PasteEnd(Pattern p, BarClipboard clip, int first)
    {
        if(clip.Meter!=p.BeatsPerBar)throw new InvalidDataException("コピー元と現在の拍子が異なります。");
        if(first<1||first+clip.Bars-1>64)throw new InvalidDataException("貼り付け先が64小節を超えます。");
        return first+clip.Bars-1;
    }
    public static void PasteBars(Pattern p, BarClipboard clip, int first)
    {
        int last=PasteEnd(p,clip,first),start=(first-1)*p.BeatsPerBar*960,end=last*p.BeatsPerBar*960;
        var notes=p.Notes.Where(n=>n.Tick<start||n.Tick>=end).Concat(clip.Notes.Select(n=>n with { Id=Guid.NewGuid(),Tick=n.Tick+start })).ToList();
        if(notes.Count>InstrumentCatalog.MaxNotes)throw new InvalidDataException("音符の上限を超えます。");
        p.Bars=Math.Max(p.Bars,last);p.Notes=notes;
    }
    public static void MoveSelection(Pattern p, IReadOnlySet<Guid> selected, int delta, int rowDelta = 0)
    {
        var moving=p.Notes.Where(n=>selected.Contains(n.Id)).ToArray();
        var occupied=p.Notes.Where(n=>!selected.Contains(n.Id)).Select(n=>(n.Instrument,n.Tick)).ToHashSet();
        if(moving.Any(n=>(int)n.Instrument+rowDelta<0||(int)n.Instrument+rowDelta>=InstrumentCatalog.Count))throw new InvalidDataException("移動先の楽器が範囲外です。");
        if(delta%240!=0 || moving.Any(n=>(long)n.Tick+delta<0||(long)n.Tick+delta>=p.Length||occupied.Contains(((Instrument)((int)n.Instrument+rowDelta),n.Tick+delta))))throw new InvalidDataException("範囲外または他の音符と重なるため、移動できません。");
        p.Notes=p.Notes.Select(n=>selected.Contains(n.Id)?n with { Tick=n.Tick+delta, Instrument=(Instrument)((int)n.Instrument+rowDelta) }:n).ToList();
    }
    public static Note[] CopyNotes(Pattern p,IReadOnlySet<Guid> selected)
    {
        var notes=p.Notes.Where(n=>selected.Contains(n.Id)).OrderBy(n=>n.Tick).ToArray();
        if(notes.Length==0)throw new InvalidDataException("コピーする音符を選択してください。");
        int start=notes.Min(n=>n.Tick);return notes.Select(n=>n with{Tick=n.Tick-start}).ToArray();
    }
    public static Guid[] PasteNotes(Pattern p,IReadOnlyList<Note> notes,int start)
    {
        if(notes.Count==0)return [];
        var added=notes.Select(n=>n with{Id=Guid.NewGuid(),Tick=checked(n.Tick+start)}).ToArray();
        if(start<0||start%240!=0||added.Any(n=>n.Tick>=p.Length))throw new InvalidDataException("貼り付け先がパターンの範囲外です。");
        var occupied=p.Notes.Select(n=>(n.Instrument,n.Tick)).ToHashSet();
        if(added.Any(n=>occupied.Contains((n.Instrument,n.Tick))))throw new InvalidDataException("貼り付け先に音符があります。空いている位置をクリックしてから貼り付けてください。");
        if(p.Notes.Count+added.Length>InstrumentCatalog.MaxNotes)throw new InvalidDataException("音符の上限を超えます。");
        p.Notes.AddRange(added);return added.Select(n=>n.Id).ToArray();
    }
    public static void RepeatBars(Pattern p,BarClipboard clip,int first,int last)
    {
        if(clip.Meter!=p.BeatsPerBar||clip.Bars<1)throw new InvalidDataException("コピー元の拍子または小節数が不正です。");
        if(first<1||last<first||last>64)throw new InvalidDataException("複製先は開始小節から64小節以内で指定してください。");
        int barTicks=p.BeatsPerBar*960,start=(first-1)*barTicks,end=last*barTicks,cycle=clip.Bars*barTicks;
        var notes=p.Notes.Where(n=>n.Tick<start||n.Tick>=end).ToList();
        for(int position=start;position<end;position+=cycle)
            notes.AddRange(clip.Notes.Where(n=>n.Tick+position<end).Select(n=>n with{Id=Guid.NewGuid(),Tick=n.Tick+position}));
        if(notes.Count>InstrumentCatalog.MaxNotes)throw new InvalidDataException("音符の上限を超えます。");
        p.Bars=Math.Max(p.Bars,last);p.Notes=notes;
    }
    public static int SetVelocities(Pattern p, IReadOnlySet<Guid> selected, int value, bool relative)
    {
        if(relative ? value is <-126 or >126 : value is <1 or >127)throw new InvalidDataException("強さの値が範囲外です。");
        int limited=0;
        p.Notes=p.Notes.Select(n=>{if(!selected.Contains(n.Id))return n;int v=relative?n.Velocity+value:value;if(v is <1 or >127)limited++;return n with { Velocity=Math.Clamp(v,1,127) };}).ToList();return limited;
    }
    public static void ValidateBars(Pattern p,int first,int last){if(first<1||last<first||last>p.Bars)throw new InvalidDataException("小節を選択してください。");}
}
