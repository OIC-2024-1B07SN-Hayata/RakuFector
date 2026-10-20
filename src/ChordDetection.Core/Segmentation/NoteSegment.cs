using ChordDetection.Core.Models;

namespace ChordDetection.Core.Segmentation;

/// <summary>A note sounding inside a segment, weighted by how long and how strongly it sounds there.</summary>
/// <param name="Pitch">MIDI note number.</param>
/// <param name="Weight">Overlap duration (s) × amplitude.</param>
/// <param name="Coverage">Fraction of the segment during which the note sounds (0..1).</param>
public sealed record SegmentNote(int Pitch, double Weight, double Coverage);

/// <summary>A time range that is analysed as one chord (stage 5 output).</summary>
public sealed record NoteSegment(double StartSeconds, double EndSeconds, IReadOnlyList<SegmentNote> Notes, int? BassPitch)
{
    public double Duration => EndSeconds - StartSeconds;

    public override string ToString() =>
        $"{StartSeconds:0.00}-{EndSeconds:0.00}s [{string.Join(" ", Notes.Select(n => PitchClasses.MidiName(n.Pitch)))}]";
}
