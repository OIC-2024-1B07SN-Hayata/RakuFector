using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrumEditor.Domain;
namespace DrumEditor.Controls;

public enum GridPart { Notes, Ruler, Velocity }

public sealed class DrumGrid : FrameworkElement
{
    public GridPart Part { get; set; }
    public Pattern Pattern { get; set; } = new();
    public Guid? Selected;
    public HashSet<Guid> SelectedIds { get; set; } = [];
    public int RangeFirst,RangeLast;
    public int CursorTick;
    public event Action<int,bool>? BarSelect;
    public event Action<Guid,bool>? MultiSelect;
    public event Action<HashSet<Guid>>? BoxSelect;
    public event Action<int>? MoveGroup;
    public event Action<int,int>? MoveAcross;
    public event Action<int>? CursorPosition;
    int ghostRow;
    Point? boxOrigin; Rect? box;
    public double PlayTick;
    public bool Playing;
    public double Step { get; set; } = 30;
    public const double Row = 48, RulerHeight = 34, VelocityHeight = 174;
    public double RowHeight { get; set; } = Row;
    public double VelocityDisplayHeight { get; set; } = VelocityHeight;
    public Instrument[] Rows => InstrumentCatalog.All.Select(d => d.Id).ToArray();
    public event Action<Instrument, int>? Add;
    public event Action<Guid, int>? Move;
    public event Action<Guid, int>? Velocity;
    public event Action<Guid>? Select;
    Guid? drag;
    double originX, originY;
    int originalValue;
    int? ghost;
    bool committing;
    public void Refresh()
    {
        Width = Pattern.Length / 240d * Step;
        Height = Part == GridPart.Ruler ? RulerHeight : Part == GridPart.Velocity ? VelocityDisplayHeight : Rows.Length * RowHeight;
        InvalidateVisual();
    }
    static Brush ColorBrush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    static readonly Brush Accent = ColorBrush("#3CD3A3"), NoteColor = ColorBrush("#35B38D"), Muted = ColorBrush("#7D8692"), White = ColorBrush("#CDD2DA"), Background = ColorBrush("#101216"), RowDark = ColorBrush("#0D1013"), RowLight = ColorBrush("#14181E");
    static readonly Pen ThinLine = new(ColorBrush("#252B33"), 1), BeatLine = new(ColorBrush("#363D46"), 1), BarLine = new(ColorBrush("#68717D"), 1), HorizontalLine = new(ColorBrush("#2A2F37"), 1);
    void Text(DrawingContext dc, string text, double x, double y, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(0, 0, Width, Height));
        var rows = Rows;
        if (Part == GridPart.Notes)
            for (int row = 0; row < rows.Length; row++)
            {
                dc.DrawRectangle(row % 2 == 0 ? RowLight : RowDark, null, new Rect(0, row * RowHeight, Width, RowHeight));
                dc.DrawLine(HorizontalLine, new Point(0, (row + 1) * RowHeight), new Point(Width, (row + 1) * RowHeight));
            }
        if(RangeFirst>0){double left=(RangeFirst-1)*Pattern.BeatsPerBar*4*Step,right=RangeLast*Pattern.BeatsPerBar*4*Step;var fill=new SolidColorBrush(Color.FromArgb(55,60,211,163));dc.DrawRectangle(fill,null,new Rect(left,0,right-left,Height));}
        var scroll = FindScrollViewer();
        double offset = scroll?.HorizontalOffset ?? 0, visible = scroll?.ViewportWidth ?? Width;
        int first = Math.Max(0, (int)(offset / Step) - 1), last = Math.Min((int)(Width / Step), (int)((offset + visible) / Step) + 2);
        for (int step = first; step <= last; step++)
        {
            double x = step * Step;
            bool bar = step % (Pattern.BeatsPerBar * 4) == 0, beat = step % 4 == 0;
            dc.DrawLine(bar ? BarLine : beat ? BeatLine : ThinLine, new Point(x, 0), new Point(x, Height));
            if (Part == GridPart.Ruler && beat && step < Width / Step && (bar || Step*4>=48) && (!bar || step/(Pattern.BeatsPerBar*4)%Math.Max(1,(int)Math.Ceiling(48/(Step*Pattern.BeatsPerBar*4)))==0))
                Text(dc, bar ? $"{step / (Pattern.BeatsPerBar * 4) + 1}" : $"{step / (Pattern.BeatsPerBar * 4) + 1}.{step / 4 % Pattern.BeatsPerBar + 1}", x + 5, 8, bar ? White : Muted);
        }
        
        if (Part != GridPart.Ruler)
            foreach (var note in Pattern.Notes)
            {
                double x = (Part == GridPart.Notes && drag == note.Id && ghost.HasValue ? ghost.Value : note.Tick) / 240d * Step;
                if (x + Step < offset || x > offset + visible) continue;
                bool selected = SelectedIds.Contains(note.Id) || note.Id == Selected;
                if(Part==GridPart.Notes && drag.HasValue && ghost.HasValue && SelectedIds.Count>1 && SelectedIds.Contains(note.Id)) x=(note.Tick+ghost.Value-originalValue)/240d*Step;
                if (Part == GridPart.Notes)
                {
                    int row = Array.IndexOf(rows, note.Instrument);
                    if(Part==GridPart.Notes && drag.HasValue && SelectedIds.Contains(note.Id))row+=ghostRow;
                    if (row < 0 || row>=rows.Length) continue;
                    if(Pattern.SwingPercent!=50||note.TimingOffsetTick!=0){double actual=Timing.EffectiveTick(Pattern,note)/240d*Step;dc.DrawLine(new Pen(White,1),new Point(actual,row*RowHeight+3),new Point(actual,(row+1)*RowHeight-3));}
                    dc.DrawRoundedRectangle(note.Instrument == Pattern.SelectedInstrument ? Accent : NoteColor, selected ? new Pen(White, 2) : null, new Rect(x + Math.Min(3,Step*.1), row * RowHeight + 6, Math.Max(.1,Step - 2*Math.Min(3,Step*.1)), RowHeight - 12), 2, 2);
                }
                else if (note.Instrument == Pattern.SelectedInstrument)
                {
                    int velocity = drag == note.Id && ghost.HasValue ? ghost.Value : note.Velocity;
                    double y = Height - 10 - velocity / 127d * (Height - 25), cx = x + Step / 2;
                    dc.DrawRectangle(Accent, null, new Rect(cx - 6, y, 12, Height - 10 - y));
                    dc.DrawEllipse(Background, new Pen(selected ? White : Accent, 2.5), new Point(cx, y), 12, 12);
                }
            }
        if(Part==GridPart.Notes)dc.DrawLine(new Pen(White,1){DashStyle=DashStyles.Dash},new Point(CursorTick/240d*Step,0),new Point(CursorTick/240d*Step,Height));
        if(box is {} area)dc.DrawRectangle(null,new Pen(Accent,1.5),area);
        // The start line remains visible while stopped, matching the design.
        double playX = PlayTick / 240 * Step;
        if (playX >= offset && playX <= offset + visible) dc.DrawLine(new Pen(Accent, 2), new Point(playX, 0), new Point(playX, Height));
    }
    System.Windows.Controls.ScrollViewer? FindScrollViewer()
    {
        DependencyObject? parent = this;
        while (parent != null) { if (parent is System.Windows.Controls.ScrollViewer scroll) return scroll; parent = VisualTreeHelper.GetParent(parent); }
        return null;
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if(Part==GridPart.Notes){var position=e.GetPosition(this);int step=3840/Pattern.GridDivision;CursorTick=Math.Clamp((int)(position.X/Step*240)/step*step,0,Pattern.Length-240);CursorPosition?.Invoke(CursorTick);Focus();InvalidateVisual();}
        base.OnMouseRightButtonDown(e);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Part == GridPart.Ruler) {int bar=(int)(e.GetPosition(this).X/(Pattern.BeatsPerBar*4*Step))+1;if(bar>=1&&bar<=Pattern.Bars)BarSelect?.Invoke(bar,(Keyboard.Modifiers&ModifierKeys.Shift)!=0);e.Handled=true;return;}
        Focus(); var position = e.GetPosition(this);
        if (position.X < 0 || position.X >= Width || position.Y < 0 || position.Y >= Height) return;
        if(Part==GridPart.Notes && (Keyboard.Modifiers&ModifierKeys.Shift)!=0){boxOrigin=position;box=new Rect(position,position);CaptureMouse();e.Handled=true;return;}
        Note? note;
        if (Part == GridPart.Velocity)
        {
            note = Pattern.Notes.FirstOrDefault(n => n.Instrument == Pattern.SelectedInstrument && Math.Abs(n.Tick / 240d * Step + Step / 2 - position.X) < 14 && Math.Abs(Height - 10 - n.Velocity / 127d * (Height - 25) - position.Y) < 16);
            if (note == null) return;
            originalValue = note.Velocity;
        }
        else
        {
            int row = (int)(position.Y / RowHeight), cell = (int)(position.X / Step) * 240;
            if (row >= Rows.Length) return;
            var instrument = Rows[row];
            CursorTick=cell;CursorPosition?.Invoke(cell);
            note = Pattern.Notes.FirstOrDefault(n=>n.Instrument==instrument&&n.Tick==cell);
            if (note == null)
            {
                int tick = (int)(position.X / Step * 240) / (3840 / Pattern.GridDivision) * (3840 / Pattern.GridDivision);
                var existing = Pattern.Notes.FirstOrDefault(n => n.Instrument == instrument && n.Tick == tick);
                if (existing != null) Select?.Invoke(existing.Id); else Add?.Invoke(instrument, tick);
                return;
            }
            originalValue = note.Tick;
        }
        ghostRow=0; drag = note.Id; originX = position.X; originY = position.Y;
        if(Part==GridPart.Notes)MultiSelect?.Invoke(note.Id,(Keyboard.Modifiers&ModifierKeys.Control)!=0);else Select?.Invoke(note.Id);
        if(Part==GridPart.Notes && (Keyboard.Modifiers&ModifierKeys.Control)!=0){drag=null;e.Handled=true;return;} CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(boxOrigin is {} from){box=new Rect(from,e.GetPosition(this));InvalidateVisual();return;}
        if (drag == null) return;
        var position = e.GetPosition(this);
        if(Part==GridPart.Notes)ghostRow=(int)Math.Floor(position.Y/RowHeight)-(int)Math.Floor(originY/RowHeight);
        ghost = Part == GridPart.Velocity ? Math.Clamp(originalValue + (int)Math.Round((originY - position.Y) * 127 / (Height - 25)), 1, 127) : originalValue + (int)Math.Round((position.X-originX)/Step*240/(3840/Pattern.GridDivision),MidpointRounding.AwayFromZero)*(3840/Pattern.GridDivision);
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if(box is {} area){var rows=Rows;BoxSelect?.Invoke(Pattern.Notes.Where(n=>{int row=Array.IndexOf(rows,n.Instrument);return row>=0&&area.Contains(new Point((n.Tick/240d+.5)*Step,(row+.5)*RowHeight));}).Select(n=>n.Id).ToHashSet());Cancel();return;}
        committing = true;
        try { if (drag is { } id && ghost is { } value) { if (Part == GridPart.Velocity) Velocity?.Invoke(id, value); else if(MoveAcross!=null)MoveAcross.Invoke(value-originalValue,ghostRow);else if(SelectedIds.Count>1)MoveGroup?.Invoke(value-originalValue);else if (value >= 0 && value < Pattern.Length) Move?.Invoke(id, value); } }
        finally { committing = false; Cancel(); }
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { if (!committing) Cancel(); }
    public void Cancel() { boxOrigin=null;box=null; drag = null; ghost = null; ghostRow=0; if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); }
}