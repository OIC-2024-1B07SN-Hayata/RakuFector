using ChordDetection.Core.Models;

namespace ChordDetection.Core.Segmentation;

public sealed record NoteSegmenterOptions
{
    /// <summary>
    /// Onsets closer than this to the first onset of a group are treated as the same strum
    /// (a strum spreads the six strings over a few tens of milliseconds).
    /// </summary>
    public double OnsetMergeWindowSeconds { get; init; } = 0.08;

    /// <summary>
    /// Segments shorter than this are merged into a neighbour (removes short noise segments).
    /// Kept below 0.3 s so that an arpeggio picked every 0.3 s still splits.
    /// </summary>
    public double MinSegmentSeconds { get; init; } = 0.25;

    /// <summary>Notes quieter than this are ignored (weak noise notes).</summary>
    public double MinAmplitude { get; init; } = 0.4;

    /// <summary>
    /// When true, an onset consisting of a single note whose pitch class is already sounding
    /// does not start a new segment (re-picked string or ghost note; the harmony is unchanged).
    /// </summary>
    public bool IgnoreSingleNoteRepeats { get; init; } = true;

    /// <summary>A note must sound for at least this fraction of the segment to be the bass.</summary>
    public double BassMinCoverage { get; init; } = 0.3;

    /// <summary>
    /// Weight multiplier for notes ringing on from an earlier chord, applied in segments that start with a new
    /// strum (2 or more notes struck together). At a chord change the old chord keeps ringing; with full weight its
    /// notes mix with the new chord (E ringing over Dm made Dm N.C. or Dm/E). These notes also cannot be the bass.
    /// Segments started by a single note (added note, arpeggio) keep their context at full weight. 1 = off.
    /// </summary>
    public double SustainedNoteWeight { get; init; } = 0.3;

    /// <summary>
    /// Notes struck less than this before the segment still count as part of its chord (e.g. the bass of a strum
    /// that was hit a moment earlier than the other strings); only older notes are treated as ringing on.
    /// </summary>
    public double RecentNoteSeconds { get; init; } = 0.6;

    /// <summary>
    /// For tone-shaped (distorted) guitar, where the played notes come out weak (see NoteDecoderOptions.ToneShaped).
    /// </summary>
    public static NoteSegmenterOptions ToneShaped { get; } = new() { MinAmplitude = 0.2 };
}

/// <summary>
/// Stage 5: splits the note list into segments at note onsets ("鳴り始め").
/// Every segment starts at a group of onsets and lasts until the next group.
/// </summary>
public sealed class NoteSegmenter
{
    private readonly NoteSegmenterOptions _options;

    public NoteSegmenter(NoteSegmenterOptions? options = null)
    {
        _options = options ?? new NoteSegmenterOptions();
    }

    public IReadOnlyList<NoteSegment> Segment(IReadOnlyList<NoteEvent> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var usable = notes
            .Where(n => n.Amplitude >= _options.MinAmplitude && n.Duration > 0)
            .OrderBy(n => n.StartSeconds)
            .ToList();
        if (usable.Count == 0) return [];

        var boundaries = FindBoundaries(usable);
        double lastEnd = usable.Max(n => n.EndSeconds);
        boundaries.Add(lastEnd);
        MergeShortSegments(boundaries);

        var segments = new List<NoteSegment>();
        for (int s = 0; s < boundaries.Count - 1; s++)
        {
            var segment = BuildSegment(usable, boundaries[s], boundaries[s + 1]);
            if (segment is not null) segments.Add(segment);
        }
        return segments;
    }

    private List<double> FindBoundaries(List<NoteEvent> sortedNotes)
    {
        // Group onsets that belong to the same strum.
        var groups = new List<List<NoteEvent>>();
        foreach (var note in sortedNotes)
        {
            if (groups.Count == 0 || note.StartSeconds - groups[^1][0].StartSeconds > _options.OnsetMergeWindowSeconds)
                groups.Add([]);
            groups[^1].Add(note);
        }

        var boundaries = new List<double>();
        foreach (var group in groups)
        {
            double start = group[0].StartSeconds;
            if (boundaries.Count > 0 && _options.IgnoreSingleNoteRepeats && group.Count == 1 &&
                IsAlreadySounding(sortedNotes, group[0], start))
            {
                continue;
            }
            boundaries.Add(start);
        }
        return boundaries;
    }

    private static bool IsAlreadySounding(List<NoteEvent> notes, NoteEvent candidate, double time) =>
        notes.Any(n => !ReferenceEquals(n, candidate) &&
                       n.StartSeconds < time && n.EndSeconds > time &&
                       n.PitchClass == candidate.PitchClass);

    /// <summary>Removes boundaries so that no segment is shorter than the minimum.</summary>
    private void MergeShortSegments(List<double> b)
    {
        bool changed = true;
        while (changed && b.Count > 2)
        {
            changed = false;
            for (int s = 0; s < b.Count - 1; s++)
            {
                if (b[s + 1] - b[s] >= _options.MinSegmentSeconds) continue;

                // First segment: merge forward. Otherwise merge into the previous segment.
                // The final boundary (end of audio) is never removed.
                b.RemoveAt(s == 0 ? 1 : s);
                changed = true;
                break;
            }
        }
    }

    private NoteSegment? BuildSegment(List<NoteEvent> notes, double start, double end)
    {
        var sounding = new List<(NoteEvent Note, double Overlap)>();
        foreach (var n in notes)
        {
            double overlap = Math.Min(n.EndSeconds, end) - Math.Max(n.StartSeconds, start);
            if (overlap > 0) sounding.Add((n, overlap));
        }
        if (sounding.Count == 0) return null;

        // Do not stretch the segment over trailing silence.
        double segmentEnd = Math.Min(end, sounding.Max(x => x.Note.EndSeconds));
        double duration = segmentEnd - start;
        if (duration <= 0) return null;

        // A new strum at the start of the segment means a new chord: notes from earlier chords are only ringing on.
        bool newStrum = sounding
            .Where(x => x.Note.StartSeconds >= start && x.Note.StartSeconds <= start + _options.OnsetMergeWindowSeconds)
            .Select(x => x.Note.Pitch).Distinct().Count() >= 2;
        bool RingingOn(NoteEvent n) => newStrum && n.StartSeconds < start - _options.RecentNoteSeconds;

        var segmentNotes = sounding
            .GroupBy(x => x.Note.Pitch)
            .Select(g => new SegmentNote(
                g.Key,
                g.Sum(x => x.Overlap * x.Note.Amplitude * (RingingOn(x.Note) ? _options.SustainedNoteWeight : 1.0)),
                Math.Min(1.0, g.Sum(x => x.Overlap) / duration)))
            .OrderBy(x => x.Pitch)
            .ToList();

        // Ringing notes of the previous chord cannot be the bass (Dm/E after E), unless nothing else qualifies.
        var current = sounding.Where(x => !RingingOn(x.Note)).Select(x => x.Note.Pitch).ToHashSet();
        var candidates = segmentNotes.Where(x => x.Coverage >= _options.BassMinCoverage).ToList();
        var bass = candidates.FirstOrDefault(x => current.Contains(x.Pitch))
                   ?? candidates.FirstOrDefault()
                   ?? segmentNotes[0];
        return new NoteSegment(start, segmentEnd, segmentNotes, bass.Pitch);
    }
}
