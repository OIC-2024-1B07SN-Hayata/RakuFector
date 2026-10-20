namespace ChordDetection.Core.Models;

/// <summary>A single detected note (stage 4 output).</summary>
/// <param name="StartSeconds">Note start time.</param>
/// <param name="EndSeconds">Note end time.</param>
/// <param name="Pitch">MIDI note number (e.g. 40 = E2, the low E string).</param>
/// <param name="Amplitude">Mean frame activation, 0..1.</param>
public sealed record NoteEvent(double StartSeconds, double EndSeconds, int Pitch, double Amplitude)
{
    public double Duration => EndSeconds - StartSeconds;

    public int PitchClass => PitchClasses.Of(Pitch);

    public override string ToString() =>
        $"{PitchClasses.MidiName(Pitch)} {StartSeconds:0.000}-{EndSeconds:0.000}s ({Amplitude:0.00})";
}
