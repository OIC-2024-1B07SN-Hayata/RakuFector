using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ChordDetection.App.ViewModels;
using ChordDetection.Core.Music;

namespace ChordDetection.App.Views;

/// <summary>
/// Guitar neck seen from the player, 1st string on top (as in TAB). Draws <see cref="Marks"/> as dots;
/// frets below <see cref="Capo"/> are dimmed and get no dots.
/// </summary>
public sealed class FretboardView : FrameworkElement
{
    private const int Strings = 6;
    private const double LeftPad = 40;   // room for the string names
    private const double RightPad = 14;
    private const double TopPad = 22;   // room for half a pill above the 1st string
    private const double BottomPad = 46; // room for the fret numbers (below the pills of the 6th string)
    private static readonly string[] StringNames = ["E", "B", "G", "D", "A", "E"];
    private static readonly int[] Inlays = [3, 5, 7, 9, 15, 17, 19, 21];

    public static readonly DependencyProperty MarksProperty = Register(nameof(Marks), typeof(IReadOnlyList<FretMarkViewModel>), null);
    public static readonly DependencyProperty MutedStringsProperty = Register(nameof(MutedStrings), typeof(IReadOnlyList<int>), null);
    public static readonly DependencyProperty CapoProperty = Register(nameof(Capo), typeof(int), 0);
    public static readonly DependencyProperty FretCountProperty = Register(nameof(FretCount), typeof(int), Fretboard.DefaultFretCount);
    public static readonly DependencyProperty BoardBrushProperty = Register(nameof(BoardBrush), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty LineBrushProperty = Register(nameof(LineBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty ScaleNoteBrushProperty = Register(nameof(ScaleNoteBrush), typeof(Brush), Brushes.SteelBlue);
    public static readonly DependencyProperty ScaleRootBrushProperty = Register(nameof(ScaleRootBrush), typeof(Brush), Brushes.DarkOrange);
    public static readonly DependencyProperty OutsideToneBrushProperty = Register(nameof(OutsideToneBrush), typeof(Brush), Brushes.HotPink);
    public static readonly DependencyProperty ScaleNoteFillBrushProperty = Register(nameof(ScaleNoteFillBrush), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty ScaleNoteRingBrushProperty = Register(nameof(ScaleNoteRingBrush), typeof(Brush), Brushes.LightGray);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(FretboardView),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<FretMarkViewModel>? Marks { get => (IReadOnlyList<FretMarkViewModel>?)GetValue(MarksProperty); set => SetValue(MarksProperty, value); }
    /// <summary>Strings (0 = 1st) marked ✕ in the open column: not played in the chord form.</summary>
    public IReadOnlyList<int>? MutedStrings { get => (IReadOnlyList<int>?)GetValue(MutedStringsProperty); set => SetValue(MutedStringsProperty, value); }
    public int Capo { get => (int)GetValue(CapoProperty); set => SetValue(CapoProperty, value); }
    public int FretCount { get => (int)GetValue(FretCountProperty); set => SetValue(FretCountProperty, value); }
    public Brush BoardBrush { get => (Brush)GetValue(BoardBrushProperty); set => SetValue(BoardBrushProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush ScaleNoteBrush { get => (Brush)GetValue(ScaleNoteBrushProperty); set => SetValue(ScaleNoteBrushProperty, value); }
    public Brush ScaleRootBrush { get => (Brush)GetValue(ScaleRootBrushProperty); set => SetValue(ScaleRootBrushProperty, value); }
    public Brush OutsideToneBrush { get => (Brush)GetValue(OutsideToneBrushProperty); set => SetValue(OutsideToneBrushProperty, value); }
    /// <summary>Fill of a scale note pill (scale view): dark, with <see cref="ScaleNoteRingBrush"/> around it.</summary>
    public Brush ScaleNoteFillBrush { get => (Brush)GetValue(ScaleNoteFillBrushProperty); set => SetValue(ScaleNoteFillBrushProperty, value); }
    public Brush ScaleNoteRingBrush { get => (Brush)GetValue(ScaleNoteRingBrushProperty); set => SetValue(ScaleNoteRingBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        int frets = Math.Max(1, FretCount);
        double boardLeft = LeftPad, boardRight = ActualWidth - RightPad;
        double top = TopPad, bottom = ActualHeight - BottomPad;
        // Column 0 (open strings) is narrower and sits left of the nut; frets 1..n share the rest.
        double fretWidth = (boardRight - boardLeft) / (frets + 0.6);
        if (fretWidth <= 4 || bottom - top <= 10) return;
        double nutX = boardLeft + fretWidth * 0.6;
        double stringGap = (bottom - top) / (Strings - 1);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double FretLineX(int fret) => nutX + fret * fretWidth;
        double CenterX(int fret) => fret == 0 ? boardLeft + fretWidth * 0.3 : FretLineX(fret) - fretWidth / 2;
        double StringY(int s) => top + s * stringGap;

        // Board, inlays
        dc.DrawRoundedRectangle(BoardBrush, null, new Rect(nutX, top - 6, boardRight - nutX, bottom - top + 12), 3, 3);
        var inlay = WithOpacity(LineBrush, 0.55);
        double midY = (top + bottom) / 2, inlayR = Math.Min(6, fretWidth * 0.12);
        foreach (int f in Inlays.Where(f => f <= frets))
            dc.DrawEllipse(inlay, null, new Point(CenterX(f), midY), inlayR, inlayR);
        if (frets >= 12)
        {
            dc.DrawEllipse(inlay, null, new Point(CenterX(12), top + stringGap * 1.5), inlayR, inlayR);
            dc.DrawEllipse(inlay, null, new Point(CenterX(12), bottom - stringGap * 1.5), inlayR, inlayR);
        }

        // Frets, nut, strings
        var fretPen = new Pen(LineBrush, 1.5);
        for (int f = 1; f <= frets; f++)
            dc.DrawLine(fretPen, new Point(FretLineX(f), top - 6), new Point(FretLineX(f), bottom + 6));
        dc.DrawLine(new Pen(LabelBrush, 5), new Point(nutX, top - 6), new Point(nutX, bottom + 6));
        for (int s = 0; s < Strings; s++)
        {
            var pen = new Pen(LabelBrush, 0.8 + s * 0.35); // lower strings are thicker
            dc.DrawLine(pen, new Point(boardLeft, StringY(s)), new Point(boardRight, StringY(s)));
            DrawText(dc, StringNames[s], 16, LabelBrush, new Point(LeftPad / 2 - 4, StringY(s)), dpi, bold: true);
        }

        // Capo: frets below it are dimmed and a bar marks the start of the capo fret.
        // Dots on the capo fret itself are the strings played "open" with the capo.
        int capo = Math.Clamp(Capo, 0, frets);
        if (capo > 0)
        {
            double dimRight = FretLineX(capo - 1);
            dc.DrawRectangle(WithOpacity(Brushes.Black, 0.5), null, new Rect(boardLeft - 4, top - 8, dimRight - boardLeft + 4, bottom - top + 16));
            dc.DrawRoundedRectangle(LabelBrush, null, new Rect(dimRight + 1, top - 10, 6, bottom - top + 20), 3, 3);
        }

        // Fret numbers
        for (int f = 0; f <= frets; f++)
        {
            bool marked = f == 12 || Inlays.Contains(f) || f == 0;
            DrawText(dc, f.ToString(CultureInfo.InvariantCulture), 16, marked ? LabelBrush : WithOpacity(LabelBrush, 0.5),
                new Point(CenterX(f), bottom + 32), dpi, bold: marked);
        }

        double r = Math.Min(stringGap * 0.44, fretWidth * 0.36);

        // Muted strings of the chord form: ✕ in the open column (at the capo when there is one)
        if (MutedStrings is { } muted)
        {
            var mutePen = new Pen(OutsideToneBrush, 2.2);
            double mx = CenterX(capo), mr = Math.Min(r * 0.45, 7);
            foreach (int s in muted.Where(s => s is >= 0 and < Strings))
            {
                double my = StringY(s);
                dc.DrawEllipse(BoardBrush, null, new Point(mx, my), mr + 3, mr + 3);
                dc.DrawLine(mutePen, new Point(mx - mr, my - mr), new Point(mx + mr, my + mr));
                dc.DrawLine(mutePen, new Point(mx - mr, my + mr), new Point(mx + mr, my - mr));
            }
        }

        // Dots
        if (Marks is not { } marks) return;

        // Scale view: pill-shaped marks with large note names. Orange = the root (of the chord being played,
        // or of the scale while no chord is), blue = chord tones, dark with a light ring = other scale notes.
        double pillH = Math.Min(stringGap * 0.8, 40);
        double pillW = Math.Min(fretWidth * 0.72, pillH * 2.2);
        double pillW0 = Math.Min(pillW, fretWidth * 0.54); // the open-string column is narrower
        double pillFont = Math.Clamp(pillH * 0.66, 13, 24);
        bool chordPlaying = marks.Any(m => m.Kind == FretMarkKind.ChordRoot);
        void Pill(Point c, int fret, Brush? fill, Pen? pen, string label, Brush text)
        {
            double w = fret == 0 ? pillW0 : pillW;
            dc.DrawRoundedRectangle(fill, pen, new Rect(c.X - w / 2, c.Y - pillH / 2, w, pillH), pillH / 2, pillH / 2);
            DrawText(dc, label, pillFont, text, c, dpi, bold: true);
        }

        foreach (var m in marks)
        {
            if (m.Fret > frets || m.StringIndex is < 0 or >= Strings) continue;
            var c = new Point(CenterX(m.Fret), StringY(m.StringIndex));
            switch (m.Kind)
            {
                case FretMarkKind.ScaleNote:
                    Pill(c, m.Fret, ScaleNoteFillBrush, new Pen(ScaleNoteRingBrush, 1.2), m.Label, Brushes.White);
                    break;
                case FretMarkKind.ScaleRoot when chordPlaying: // orange is kept for the chord's root
                    Pill(c, m.Fret, ScaleNoteFillBrush, new Pen(ScaleRootBrush, 1.8), m.Label, ScaleRootBrush);
                    break;
                case FretMarkKind.ScaleRoot:
                case FretMarkKind.ChordRoot:
                    Pill(c, m.Fret, ScaleRootBrush, null, m.Label, Brushes.White);
                    break;
                case FretMarkKind.ChordTone:
                    Pill(c, m.Fret, ScaleNoteBrush, null, m.Label, Brushes.White);
                    break;
                case FretMarkKind.ChordToneOutsideScale:
                    Pill(c, m.Fret, BoardBrush, new Pen(OutsideToneBrush, 2) { DashStyle = DashStyles.Dash }, m.Label, OutsideToneBrush);
                    break;
                // Chord-form view, in the same pill style: fretted notes filled (root orange, others blue),
                // the chord's notes elsewhere dark with a ring (the root's ring orange).
                case FretMarkKind.FormRoot:
                    Pill(c, m.Fret, ScaleRootBrush, null, m.Label, Brushes.White);
                    break;
                case FretMarkKind.FormTone:
                    Pill(c, m.Fret, ScaleNoteBrush, null, m.Label, Brushes.White);
                    break;
                case FretMarkKind.OtherRoot:
                    Pill(c, m.Fret, ScaleNoteFillBrush, new Pen(ScaleRootBrush, 1.8), m.Label, ScaleRootBrush);
                    break;
                case FretMarkKind.OtherTone:
                    Pill(c, m.Fret, ScaleNoteFillBrush, new Pen(ScaleNoteRingBrush, 1.2), m.Label, Brushes.White);
                    break;
            }
        }
    }

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        var copy = brush.Clone();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    /// <summary>Draws <paramref name="text"/> centred on <paramref name="center"/>.</summary>
    private void DrawText(DrawingContext dc, string text, double size, Brush brush, Point center, double dpi, bool bold)
    {
        var typeface = new Typeface(new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI"), FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}
