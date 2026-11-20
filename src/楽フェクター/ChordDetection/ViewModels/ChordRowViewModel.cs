using ChordDetection.Core.Chords;

namespace ChordDetection.App.ViewModels;

/// <summary>One chord in the timeline.</summary>
public sealed class ChordRowViewModel
{
    /// <param name="chord">The sounding chord (already transposed).</param>
    /// <param name="name">Its name spelled for the key, e.g. "Bb".</param>
    /// <param name="degree">Degree shown below the name, e.g. "♭Ⅶ" (ディグリーを表示); empty otherwise.</param>
    /// <param name="shape">Shape to fret with the capo, e.g. "G"; null without a capo.</param>
    /// <param name="pixelsPerSecond">Timeline scale (タイムラインの幅).</param>
    public ChordRowViewModel(DisplayedChord chord, string name, string degree, string? shape, string notes, double pixelsPerSecond)
    {
        PixelsPerSecond = pixelsPerSecond;
        StartSeconds = chord.StartSeconds;
        EndSeconds = chord.EndSeconds;
        Name = name;
        Degree = degree;
        ShapeText = shape is null ? "" : $"押さえる形 {shape}";
        Notes = notes;
        Color = RootColor(chord.Chord);
    }

    /// <summary>Timeline scale the row was laid out with.</summary>
    public double PixelsPerSecond { get; }

    public double StartSeconds { get; }
    public double EndSeconds { get; }
    /// <summary>Sounding chord name.</summary>
    public string Name { get; }

    /// <summary>Degree below the name, e.g. "Ⅵm"; empty when not shown.</summary>
    public string Degree { get; }

    public bool HasDegree => Degree.Length > 0;

    /// <summary>"押さえる形 G" with a capo; empty without.</summary>
    public string ShapeText { get; }

    /// <summary>Detected notes, e.g. "E2 B2 E3".</summary>
    public string Notes { get; }

    public string StartText => FormatTime(StartSeconds);
    public string EndText => FormatTime(EndSeconds);
    public string DurationText => $"{EndSeconds - StartSeconds:0.00}s";

    /// <summary>Position in the timeline (N.C. is not shown, so its time stays empty).</summary>
    public double Left => StartSeconds * PixelsPerSecond;

    /// <summary>Width in the timeline (proportional to duration), with a small gap to the next chord.</summary>
    public double Width => Math.Max(3, (EndSeconds - StartSeconds) * PixelsPerSecond - 1);

    /// <summary>Tooltip: time, detailed-level names and notes.</summary>
    public string ToolTip => $"{Name}  {StartText} – {EndText}（{DurationText}）\n音: {Notes}"
        + (HasDegree ? $"\nディグリー {Degree}" : "")
        + (ShapeText.Length > 0 ? $"\n{ShapeText}" : "");

    /// <summary>Background colour; the hue follows the root so equal chords look alike.</summary>
    public string Color { get; }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    private static string RootColor(ChordResult chord)
    {
        if (chord.IsNoChord) return "#9E9E9E";
        // Circle of fifths order so neighbouring keys get neighbouring hues.
        double hue = (chord.Root * 7 % 12) * 30.0;
        double lightness = chord.Type!.Intervals.Contains(3) ? 0.40 : 0.48; // minor-ish chords darker
        return HslToHex(hue, 0.55, lightness);
    }

    private static string HslToHex(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static int To255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{To255(r + m):X2}{To255(g + m):X2}{To255(b + m):X2}";
    }
}
