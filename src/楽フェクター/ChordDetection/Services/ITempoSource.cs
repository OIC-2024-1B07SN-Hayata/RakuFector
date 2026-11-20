namespace ChordDetection.App.Services;

/// <summary>
/// The app's tempo (BPM / meter) and the metronome / drums played with it. Keeps the drum editor out of the
/// ViewModel; the shell connects it (FXForge.Integration.DrumTempoSource).
/// </summary>
public interface ITempoSource
{
    int Bpm { get; }

    /// <summary>Quarter-note beats per bar.</summary>
    int BeatsPerBar { get; }

    /// <summary>Whether the metronome click sounds with the drums.</summary>
    bool MetronomeEnabled { get; set; }

    /// <summary>Level of the drums (0–1).</summary>
    double DrumVolume { get; set; }

    /// <summary>Level of the metronome click (0–1).</summary>
    double MetronomeVolume { get; set; }

    /// <summary>
    /// Raised on the UI thread when <see cref="Bpm"/>, <see cref="BeatsPerBar"/> or <see cref="MetronomeEnabled"/>
    /// changes.
    /// </summary>
    event Action? TempoChanged;

    /// <summary>Delay from <see cref="StartAsync"/>'s timestamp until bar 1 is heard.</summary>
    double OutputLatencySeconds { get; }

    /// <summary>Starts the metronome / drums from bar 1.</summary>
    /// <returns>Stopwatch timestamp at which bar 1 began; null when it could not start.</returns>
    Task<long?> StartAsync();

    void Stop();
}
