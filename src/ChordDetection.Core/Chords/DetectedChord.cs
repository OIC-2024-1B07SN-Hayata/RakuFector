using ChordDetection.Core.Segmentation;

namespace ChordDetection.Core.Chords;

/// <summary>Classification result of one segment, always at the detailed level.</summary>
public sealed record DetectedChord(double StartSeconds, double EndSeconds, ChordResult Chord, NoteSegment Segment);
