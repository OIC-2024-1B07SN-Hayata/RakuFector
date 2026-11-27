using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ChordDetection.App.ViewModels;
using ChordDetection.Core.Music;

namespace ChordDetection.App.Views;

/// <summary>
/// Small upright chord diagram: 6th string on the left, 5 frets, ✕ / ○ above the nut, the fretted notes as dots
/// (<see cref="Dots"/>, frets as in the form) and a barre when the lowest fret is held across the strings.
/// </summary>
public sealed class ChordDiagramView : FrameworkElement
{
    private const int Strings = 6;
    private const int ShownFrets = 5;
    private const double TopPad = 14;    // ✕ / ○ row
    private const double LeftPad = 22;   // starting fret number
    private const double RightPad = 8;
    private const double BottomPad = 6;

    public static readonly DependencyProperty FormProperty = Register(nameof(Form), typeof(ChordForm), null);
    public static readonly DependencyProperty DotsProperty = Register(nameof(Dots), typeof(IReadOnlyList<FretMarkViewModel>), null);
    public static readonly DependencyProperty LineBrushProperty = Register(nameof(LineBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty ToneBrushProperty = Register(nameof(ToneBrush), typeof(Brush), Brushes.SteelBlue);
    public static readonly DependencyProperty RootBrushProperty = Register(nameof(RootBrush), typeof(Brush), Brushes.DarkOrange);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(ChordDiagramView),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public ChordForm? Form { get => (ChordForm?)GetValue(FormProperty); set => SetValue(FormProperty, value); }
    /// <summary>Fretted notes; <see cref="FretMarkViewModel.Fret"/> is the form's fret (without the capo).</summary>
    public IReadOnlyList<FretMarkViewModel>? Dots { get => (IReadOnlyList<FretMarkViewModel>?)GetValue(DotsProperty); set => SetValue(DotsProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush ToneBrush { get => (Brush)GetValue(ToneBrushProperty); set => SetValue(ToneBrushProperty, value); }
    public Brush RootBrush { get => (Brush)GetValue(RootBrushProperty); set => SetValue(RootBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        if (Form is not { } form) return;
        double left = LeftPad, right = ActualWidth - RightPad, top = TopPad, bottom = ActualHeight - BottomPad;
        double stringGap = (right - left) / (Strings - 1), fretGap = (bottom - top) / ShownFrets;
        if (stringGap <= 4 || fretGap <= 4) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Frets 1–5 for open-position forms, otherwise start at the lowest fretted fret.
        int startFret = form.MaxFret <= ShownFrets ? 1 : form.MinFret;
        double X(int stringIndex) => left + (Strings - 1 - stringIndex) * stringGap; // 6th string on the left
        double Y(int fret) => top + (fret - startFret + 0.5) * fretGap;

        var linePen = new Pen(LineBrush, 1.2);
        for (int f = 0; f <= ShownFrets; f++)
            dc.DrawLine(linePen, new Point(left, top + f * fretGap), new Point(right, top + f * fretGap));
        for (int s = 0; s < Strings; s++)
            dc.DrawLine(linePen, new Point(X(s), top), new Point(X(s), bottom));
        if (startFret == 1)
            dc.DrawLine(new Pen(LabelBrush, 4), new Point(left - 1, top), new Point(right + 1, top));
        else
            DrawText(dc, startFret.ToString(CultureInfo.InvariantCulture), 13, LabelBrush, new Point(left - 12, Y(startFret)), dpi, bold: true);

        // ✕ / ○ above the nut
        var markPen = new Pen(LabelBrush, 1.5);
        double m = Math.Min(4, stringGap * 0.2), markY = top - 7;
        for (int s = 0; s < Strings; s++)
        {
            if (form.FretOn(s) is null)
            {
                dc.DrawLine(markPen, new Point(X(s) - m, markY - m), new Point(X(s) + m, markY + m));
                dc.DrawLine(markPen, new Point(X(s) - m, markY + m), new Point(X(s) + m, markY - m));
            }
            else if (form.FretOn(s) == 0)
            {
                dc.DrawEllipse(null, markPen, new Point(X(s), markY), m, m);
            }
        }

        double r = Math.Min(stringGap, fretGap) * 0.47;

        // Barre: the lowest fret held from the lowest played string to the 1st string
        int minFret = form.MinFret;
        int lowest = Enumerable.Range(0, Strings).Last(s => form.FretOn(s) is not null);
        if (minFret > 0 && form.FretOn(0) == minFret && form.FretOn(lowest) == minFret && lowest > 0
            && Enumerable.Range(0, lowest + 1).All(s => form.FretOn(s) is null or > 0))
        {
            double y = Y(minFret);
            dc.DrawRoundedRectangle(WithOpacity(LabelBrush, 0.35), null,
                new Rect(X(lowest) - r, y - r * 0.75, X(0) - X(lowest) + 2 * r, r * 1.5), r * 0.75, r * 0.75);
        }

        if (Dots is not { } dots) return;
        double fontSize = Math.Clamp(r * 1.1, 7, 16);
        foreach (var d in dots)
        {
            if (d.Fret < startFret || d.Fret >= startFret + ShownFrets || d.StringIndex is < 0 or >= Strings) continue;
            var c = new Point(X(d.StringIndex), Y(d.Fret));
            dc.DrawEllipse(d.Kind == FretMarkKind.FormRoot ? RootBrush : ToneBrush, null, c, r, r);
            DrawText(dc, d.Label, fontSize, Brushes.White, c, dpi, bold: true);
        }
    }

    private static Brush WithOpacity(Brush brush, double opacity)
    {
        var copy = brush.Clone();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    private void DrawText(DrawingContext dc, string text, double size, Brush brush, Point center, double dpi, bool bold)
    {
        var typeface = new Typeface(new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI"), FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, dpi);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }
}
