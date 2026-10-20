using ChordDetection.Core.Chords;

namespace ChordDetection.App.ViewModels;

/// <summary>One chord in the timeline.</summary>
public sealed class ChordRowViewModel
{
    /// <summary>Timeline scale (the design shows one second per 174 px).</summary>
    public const double PixelsPerSecond = 174;

    public ChordRowViewModel(DisplayedChord chord, string notes)
    {
        StartSeconds = chord.StartSeconds;
        EndSeconds = chord.EndSeconds;
        Name = chord.Name;
        Notes = notes;
        Color = RootColor(chord.Chord);
    }

    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public string Name { get; }

    /// <summary>Detected notes, e.g. "E2 B2 E3".</summary>
    public string Notes { get; }

    public string StartText => FormatTime(StartSeconds);
    public string EndText => FormatTime(EndSeconds);
    public string DurationText => $"{EndSeconds - StartSeconds:0.00}s";

    /// <summary>Position in the timeline (N.C. is not shown, so its time stays empty).</summary>
    public double Left => StartSeconds * PixelsPerSecond;

    /// <summary>Width in the timeline (proportional to duration), with a small gap to the next chord.</summary>
    public double Width => Math.Max(24, (EndSeconds - StartSeconds) * PixelsPerSecond - 2);

    /// <summary>Tooltip: time, detailed-level names and notes.</summary>
    public string ToolTip => $"{StartText} – {EndText}（{DurationText}）\n音: {Notes}";

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
