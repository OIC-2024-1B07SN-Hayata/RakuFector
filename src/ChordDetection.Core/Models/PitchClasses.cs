namespace ChordDetection.Core.Models;

/// <summary>Pitch class (0 = C ... 11 = B) helpers.</summary>
public static class PitchClasses
{
    // Spelling commonly used on guitar chord charts.
    private static readonly string[] Names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B"];

    private static readonly Dictionary<string, int> Parse = new(StringComparer.Ordinal)
    {
        ["C"] = 0, ["B#"] = 0,
        ["C#"] = 1, ["Db"] = 1,
        ["D"] = 2,
        ["D#"] = 3, ["Eb"] = 3,
        ["E"] = 4, ["Fb"] = 4,
        ["F"] = 5, ["E#"] = 5,
        ["F#"] = 6, ["Gb"] = 6,
        ["G"] = 7,
        ["G#"] = 8, ["Ab"] = 8,
        ["A"] = 9,
        ["A#"] = 10, ["Bb"] = 10,
        ["B"] = 11, ["Cb"] = 11,
    };

    public static int Of(int midiPitch) => ((midiPitch % 12) + 12) % 12;

    public static string Name(int pitchClass) => Names[Of(pitchClass)];

    /// <summary>MIDI number to name with octave, e.g. 40 → "E2".</summary>
    public static string MidiName(int midiPitch) => $"{Name(midiPitch)}{midiPitch / 12 - 1}";

    /// <summary>Parses a note name such as "C#", "Bb" or "E".</summary>
    public static bool TryParse(string text, out int pitchClass) => Parse.TryGetValue(text, out pitchClass);
}
