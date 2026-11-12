using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>
/// Turns pitch classes into names for display. Inside the app notes are numbers (C = 0 ... B = 11);
/// only here is # or b chosen, by the key. Without a key, names use #.
/// </summary>
public static class NoteSpeller
{
    private static readonly string[] Sharps = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] Flats = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    /// <summary>Note name; b in flat keys, # otherwise (also when the key is unknown).</summary>
    public static string Name(int pitchClass, MusicKey? key = null) =>
        (key is { UsesFlats: true } ? Flats : Sharps)[PitchClasses.Of(pitchClass)];

    /// <summary>Note name that always uses #.</summary>
    public static string SharpName(int pitchClass) => Sharps[PitchClasses.Of(pitchClass)];

    /// <summary>MIDI number to name with octave, e.g. 46 → "Bb2" in F major.</summary>
    public static string MidiName(int midiPitch, MusicKey? key = null) =>
        $"{Name(midiPitch, key)}{(int)Math.Floor(midiPitch / 12.0) - 1}";

    /// <summary>Chord name such as "Bb", "D/F#" or "N.C.", spelled for the key.</summary>
    public static string ChordName(ChordResult chord, MusicKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord) return ChordResult.NoChordName;
        var name = Name(chord.Root, key) + chord.Type!.Symbol;
        return chord.IsInversion ? $"{name}/{Name(chord.Bass!.Value, key)}" : name;
    }

    /// <summary>Chord name that always uses # (shapes: open chord names never contain b).</summary>
    public static string SharpChordName(ChordResult chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord) return ChordResult.NoChordName;
        var name = SharpName(chord.Root) + chord.Type!.Symbol;
        return chord.IsInversion ? $"{name}/{SharpName(chord.Bass!.Value)}" : name;
    }
}
