namespace ChordDetection.Core.Chords;

/// <summary>How detailed the chord names are shown. Values match <see cref="ChordType.Level"/>.</summary>
public enum DisplayLevel
{
    /// <summary>Major / minor / power chord only, no slash bass.</summary>
    Basic = 1,

    /// <summary>All catalog entries, with slash bass (C/E).</summary>
    Detailed = 2,
}
