using ChordDetection.Core.Models;

namespace ChordDetection.Core.Chords;

/// <summary>
/// A classified chord. The bass is kept separately from the root (design ④)
/// so that inversions (C/E) can be displayed.
/// </summary>
public sealed record ChordResult(int Root, ChordType? Type, int? Bass, double Score)
{
    public const string NoChordName = "N.C.";

    public static ChordResult NoChord { get; } = new(0, null, null, double.NegativeInfinity);

    public bool IsNoChord => Type is null;

    /// <summary>True when the lowest note is not the root (an inversion / slash chord).</summary>
    public bool IsInversion => !IsNoChord && Bass is { } b && b != Root;

    /// <summary>Chord name such as "Am7", "C/E", "E5" or "N.C.".</summary>
    public string Name
    {
        get
        {
            if (IsNoChord) return NoChordName;
            var name = PitchClasses.Name(Root) + Type!.Symbol;
            return IsInversion ? $"{name}/{PitchClasses.Name(Bass!.Value)}" : name;
        }
    }

    /// <summary>Name without the slash bass.</summary>
    public string NameWithoutBass => IsNoChord ? NoChordName : PitchClasses.Name(Root) + Type!.Symbol;

    public override string ToString() => Name;
}
