namespace ChordDetection.Core.Music;

/// <summary>
/// Bar positions of a performance played to a steady tempo (the app's metronome / drum pattern).
/// Bar 1 starts at <see cref="OffsetSeconds"/>; every bar is <see cref="BeatsPerBar"/> quarter-note beats long.
/// </summary>
public sealed record BarGrid
{
    public const int MinBpm = 1;
    public const int MaxBpm = 300;

    public BarGrid(double bpm, int beatsPerBar, double offsetSeconds = 0)
    {
        if (!double.IsFinite(bpm) || bpm < MinBpm || bpm > MaxBpm)
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, $"BPM must be {MinBpm}–{MaxBpm}.");
        if (beatsPerBar < 1) throw new ArgumentOutOfRangeException(nameof(beatsPerBar), beatsPerBar, "At least one beat per bar.");
        if (!double.IsFinite(offsetSeconds)) throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
        Bpm = bpm;
        BeatsPerBar = beatsPerBar;
        OffsetSeconds = offsetSeconds;
    }

    public double Bpm { get; }
    public int BeatsPerBar { get; }

    /// <summary>Time of the first beat of bar 1 (may be negative when the recording started after it).</summary>
    public double OffsetSeconds { get; }

    public double BeatSeconds => 60.0 / Bpm;
    public double BarSeconds => BeatSeconds * BeatsPerBar;

    /// <summary>Start of the bar with zero-based <paramref name="index"/>.</summary>
    public double BarStart(int index) => OffsetSeconds + index * BarSeconds;

    /// <summary>Zero-based index of the bar that contains <paramref name="seconds"/> (negative before bar 1).</summary>
    public int BarIndexAt(double seconds) => (int)Math.Floor((seconds - OffsetSeconds) / BarSeconds);

    /// <summary>Start of the bar line (also before bar 1) nearest to <paramref name="seconds"/>.</summary>
    public double NearestBarStart(double seconds) => NearestLine(seconds, 1);

    /// <summary>
    /// Nearest of <paramref name="linesPerBar"/> evenly spaced lines per bar (1 = bar lines, <see cref="BeatsPerBar"/> = beats),
    /// also before bar 1.
    /// </summary>
    public double NearestLine(double seconds, int linesPerBar)
    {
        if (linesPerBar < 1) throw new ArgumentOutOfRangeException(nameof(linesPerBar), linesPerBar, "At least one line per bar.");
        double step = BarSeconds / linesPerBar;
        return OffsetSeconds + Math.Round((seconds - OffsetSeconds) / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Number of bars (from bar 1) needed to cover audio of <paramref name="durationSeconds"/>.</summary>
    public int BarCount(double durationSeconds) =>
        durationSeconds <= OffsetSeconds ? 0 : (int)Math.Ceiling((durationSeconds - OffsetSeconds) / BarSeconds);
}
