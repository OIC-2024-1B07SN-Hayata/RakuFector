using ChordDetection.Core;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;
using ChordDetection.Core.Segmentation;
using ChordDetection.Infrastructure.Audio;
using ChordDetection.Infrastructure.Separation;

namespace ChordDetection.Infrastructure.Chroma;

public sealed record ChromaOptions
{
    /// <summary>FFT size. 8192 samples (0.37 s) separates semitones down to the low E string (82 Hz, ±2.4 Hz per semitone).</summary>
    public int FrameSize { get; init; } = 8192;

    /// <summary>Distance between frames (0.09 s at 22,050 Hz).</summary>
    public int HopSize { get; init; } = 2048;

    /// <summary>Lowest frequency used (Hz). Below the low E string (82 Hz).</summary>
    public double MinFrequencyHz { get; init; } = 75;

    /// <summary>Highest frequency used (Hz), the same upper limit as the Basic Pitch path (most overtones lie above).</summary>
    public double MaxFrequencyHz { get; init; } = 1000;

    /// <summary>Peaks up to this frequency are bass candidates (B3 247 Hz: up to the open D/G strings' range).</summary>
    public double BassMaxFrequencyHz { get; init; } = 260;

    /// <summary>The bass is the lowest peak at least this strong relative to the strongest peak in the bass range.</summary>
    public double BassMinRelativeLevel { get; init; } = 0.3;

    /// <summary>
    /// A peak this much weaker than a peak at 1/2 … 1/<see cref="MaxOvertone"/> of its frequency is treated as that note's overtone and
    /// weakened by <see cref="OvertoneWeight"/> (the same idea as <see cref="HarmonicFilter"/>).
    /// </summary>
    public double OvertoneMaxRelativeLevel { get; init; } = 1.0;

    /// <summary>
    /// Highest overtone number checked. 6 includes the 5th overtone, a major third two octaves up
    /// (it turned E5 into E), but stays below the 7th, which falls between semitones.
    /// </summary>
    public int MaxOvertone { get; init; } = 6;

    /// <summary>Weight multiplier for peaks judged to be overtones. 1 = off.</summary>
    public double OvertoneWeight { get; init; } = 0.2;

    /// <summary>Frames quieter than this (dBFS, RMS) are silent: no chord.</summary>
    public double SilenceThresholdDb { get; init; } = -50;

    /// <summary>Frames on each side averaged with a frame before classification (2 → 5 frames, 0.46 s).</summary>
    public int SmoothingFrames { get; init; } = 2;

    /// <summary>Segments shorter than this are merged into a neighbour (the same value as <see cref="NoteSegmenterOptions.MinSegmentSeconds"/>).</summary>
    public double MinSegmentSeconds { get; init; } = 0.25;

    /// <summary>
    /// N.C. stretches up to this long between two chords, with sound throughout, are the change itself
    /// (frames that mix the old and the new chord); they are split between the two chords.
    /// </summary>
    public double MaxTransitionSeconds { get; init; } = 0.7;
}

/// <summary>
/// Comparison baseline for the Basic Pitch path: chords from a chroma (the strength of the 12 pitch classes per frame)
/// instead of a note list. FFT → spectral peaks → 12 pitch classes + lowest strong peak as bass → the same
/// <see cref="IChordClassifier"/> as the main path → consecutive frames with the same chord form a segment.
/// No model and no 2-second window, so it is light and has a short delay, but overtones mix into the 12 classes.
/// </summary>
public sealed class ChromaChordDetector
{
    private readonly ChromaOptions _options;
    private readonly IChordClassifier _classifier;
    private readonly int _sampleRate = BasicPitchConstants.SampleRate;

    public ChromaChordDetector(IChordClassifier? classifier = null, ChromaOptions? options = null)
    {
        _options = options ?? new ChromaOptions();
        _classifier = classifier ?? new RuleBasedChordClassifier();
    }

    /// <param name="monoAudio">Preprocessed audio (mono, 22,050 Hz), as for <see cref="ChordDetectionService"/>.</param>
    public ChordAnalysis Analyze(float[] monoAudio, CancellationToken cancellationToken = default) =>
        AnalyzeStreaming([monoAudio], null, cancellationToken);

    /// <summary>
    /// Mono pieces (22,050 Hz) are analysed as they arrive; the chords so far are reported after every piece.
    /// Only new frames are computed, and the final result equals <see cref="Analyze"/> on the whole audio.
    /// </summary>
    public ChordAnalysis AnalyzeStreaming(
        IEnumerable<float[]> monoPieces,
        IProgress<ChordAnalysis>? updates = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monoPieces);
        var fft = new Fft(_options.FrameSize);
        var window = Enumerable.Range(0, _options.FrameSize)
            .Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / _options.FrameSize)).ToArray();
        var audio = new FloatBuffer();   // samples from index audioStart on
        long audioStart = 0;
        var frames = new List<Frame>();
        var labels = new List<string>();
        long samples = 0;

        foreach (var piece in monoPieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            audio.Append(piece);
            samples += piece.Length;
            // Frame i is centred on sample i * hop; it is computed once all of its samples have arrived.
            while ((long)frames.Count * _options.HopSize + _options.FrameSize / 2 <= samples)
                frames.Add(ComputeFrame(audio, audioStart, frames.Count, fft, window));

            // Samples before the next frame are no longer needed.
            long keepFrom = Math.Max(0, (long)frames.Count * _options.HopSize - _options.FrameSize / 2);
            int drop = (int)Math.Min(audio.Count, keepFrom - audioStart);
            if (drop > 0)
            {
                audio.RemoveFront(drop);
                audioStart += drop;
            }
            if (updates is not null)
            {
                UpdateLabels(frames, labels);
                updates.Report(Build(frames, labels, (double)samples / _sampleRate));
            }
        }

        // Frames whose centre is still inside the audio, reading zeros past the end.
        while ((long)frames.Count * _options.HopSize < samples)
            frames.Add(ComputeFrame(audio, audioStart, frames.Count, fft, window));
        UpdateLabels(frames, labels);
        return Build(frames, labels, (double)samples / _sampleRate);
    }

    /// <summary>Spectrum of one frame reduced to 12 pitch-class weights and a bass pitch.</summary>
    private sealed record Frame(double[] Chroma, int? BassPitch, bool Silent);

    private Frame ComputeFrame(FloatBuffer audio, long audioStart, int index, Fft fft, double[] window)
    {
        int n = _options.FrameSize;
        long start = (long)index * _options.HopSize - n / 2;   // frame centred on index * hop
        var re = new double[n];
        var im = new double[n];
        double energy = 0;
        for (int i = 0; i < n; i++)
        {
            long src = start + i - audioStart;
            double x = src >= 0 && src < audio.Count ? audio[(int)src] : 0;
            energy += x * x;
            re[i] = x * window[i];
        }

        var chroma = new double[12];
        double rmsDb = 10 * Math.Log10(energy / n + 1e-20);
        if (rmsDb < _options.SilenceThresholdDb) return new Frame(chroma, null, true);

        fft.Transform(re, im, inverse: false);
        var mag = new double[n / 2];
        for (int k = 0; k < mag.Length; k++) mag[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);

        var peaks = FindPeaks(mag);
        WeakenOvertones(peaks);

        // Strongest peak per pitch class, not the sum over octaves (as in PitchClassProfile.FromSegment): the root doubled
        // in two octaves of a power chord would otherwise make the fifth look weak (E5 became N.C.).
        foreach (var p in peaks)
        {
            int pc = PitchClasses.Of(p.Pitch);
            chroma[pc] = Math.Max(chroma[pc], p.Weight);
        }
        return new Frame(chroma, FindBass(peaks), false);
    }

    private sealed class Peak(double hz, int pitch, double magnitude)
    {
        public double Hz { get; } = hz;
        public int Pitch { get; } = pitch;
        public double Magnitude { get; } = magnitude;
        public double Weight { get; set; } = magnitude;
    }

    /// <summary>Local maxima of the magnitude spectrum, at the frequency refined by parabolic interpolation.</summary>
    private List<Peak> FindPeaks(double[] mag)
    {
        double binHz = (double)_sampleRate / _options.FrameSize;
        int kMin = Math.Max(1, (int)(_options.MinFrequencyHz / binHz));
        int kMax = Math.Min(mag.Length - 2, (int)(_options.MaxFrequencyHz / binHz) + 1);
        double floor = mag.Skip(kMin).Take(kMax - kMin).DefaultIfEmpty(0).Max() * 0.02;

        var peaks = new List<Peak>();
        for (int k = kMin; k <= kMax; k++)
        {
            double a = mag[k - 1], b = mag[k], c = mag[k + 1];
            if (!(b > a && b >= c) || b < floor) continue;

            double denom = a - 2 * b + c;
            double offset = denom == 0 ? 0 : 0.5 * (a - c) / denom;
            double hz = (k + offset) * binHz;
            if (hz < _options.MinFrequencyHz || hz > _options.MaxFrequencyHz) continue;

            double midi = 69 + 12 * Math.Log2(hz / 440.0);
            int pitch = (int)Math.Round(midi);
            if (Math.Abs(midi - pitch) > 0.4) continue;   // between two semitones: not a played note
            peaks.Add(new Peak(hz, pitch, b));
        }
        return peaks;
    }

    /// <summary>A peak near 2 … <see cref="ChromaOptions.MaxOvertone"/> times the frequency of a stronger peak is probably its overtone.</summary>
    private void WeakenOvertones(List<Peak> peaks)
    {
        if (_options.OvertoneWeight >= 1) return;
        foreach (var p in peaks)
        {
            foreach (var lower in peaks)
            {
                if (lower.Hz >= p.Hz) break;
                double ratio = p.Hz / lower.Hz;
                int h = (int)Math.Round(ratio);
                if (h < 2 || h > _options.MaxOvertone || Math.Abs(ratio - h) > 0.03 * h) continue;
                if (p.Magnitude <= lower.Magnitude * _options.OvertoneMaxRelativeLevel)
                {
                    p.Weight = p.Magnitude * _options.OvertoneWeight;
                    break;
                }
            }
        }
    }

    private int? FindBass(List<Peak> peaks)
    {
        var low = peaks.Where(p => p.Hz <= _options.BassMaxFrequencyHz).ToList();
        if (low.Count == 0) return peaks.Count > 0 ? peaks.MaxBy(p => p.Weight)!.Pitch : null;
        double strongest = low.Max(p => p.Magnitude);
        return low.First(p => p.Magnitude >= strongest * _options.BassMinRelativeLevel).Pitch;
    }

    /// <summary>
    /// Chord name of every frame from the chroma averaged over the neighbouring frames (silent frames are N.C.).
    /// A new frame only changes the labels of the last <see cref="ChromaOptions.SmoothingFrames"/> frames, so only those
    /// and the new ones are computed.
    /// </summary>
    private void UpdateLabels(List<Frame> frames, List<string> labels)
    {
        int s = _options.SmoothingFrames;
        int from = Math.Max(0, labels.Count - s);
        labels.RemoveRange(from, labels.Count - from);
        for (int i = from; i < frames.Count; i++)
        {
            if (frames[i].Silent) { labels.Add(ChordResult.NoChord.Name); continue; }
            int lo = Math.Max(0, i - s), hi = Math.Min(frames.Count, i + s + 1);
            var range = Enumerable.Range(lo, hi - lo).Where(j => !frames[j].Silent).ToList();
            labels.Add(_classifier.Classify(Profile(frames, range)).Name);
        }
    }

    /// <summary>Join equal neighbouring labels, merge short segments, classify each segment again on its whole length.</summary>
    private ChordAnalysis Build(List<Frame> frames, List<string> labels, double durationSeconds)
    {
        double hopSeconds = (double)_options.HopSize / _sampleRate;

        // 1. Runs of equal labels; short runs join the previous run (the first one joins the next).
        var runs = new List<(int From, int To)>();
        for (int i = 0; i < frames.Count; i++)
        {
            if (runs.Count > 0 && labels[runs[^1].From] == labels[i]) runs[^1] = (runs[^1].From, i + 1);
            else runs.Add((i, i + 1));
        }
        // N.C. between two chords while sound goes on: the change itself, given half to each side.
        int maxTransition = (int)Math.Round(_options.MaxTransitionSeconds / hopSeconds);
        for (int r = runs.Count - 2; r >= 1; r--)
        {
            var (from, to) = runs[r];
            if (labels[from] != ChordResult.NoChord.Name || to - from > maxTransition) continue;
            if (Enumerable.Range(from, to - from).Any(j => frames[j].Silent)) continue;
            int mid = (from + to + 1) / 2;
            runs[r - 1] = (runs[r - 1].From, mid);
            runs[r + 1] = (mid, runs[r + 1].To);
            runs.RemoveAt(r);
        }

        int minFrames = Math.Max(1, (int)Math.Round(_options.MinSegmentSeconds / hopSeconds));
        bool changed = true;
        while (changed && runs.Count > 1)
        {
            changed = false;
            for (int r = 0; r < runs.Count; r++)
            {
                if (runs[r].To - runs[r].From >= minFrames) continue;
                if (r == 0) runs[1] = (runs[0].From, runs[1].To);
                else runs[r - 1] = (runs[r - 1].From, runs[r].To);
                runs.RemoveAt(r);
                changed = true;
                break;
            }
        }

        // 2. Classify each run on its whole length; join neighbours that end up with the same chord.
        var chords = new List<DetectedChord>();
        foreach (var (from, to) in runs)
        {
            double start = Math.Max(0, (from - 0.5) * hopSeconds);
            double end = Math.Min(durationSeconds, (to - 0.5) * hopSeconds);
            if (end <= start) continue;

            var sounding = Enumerable.Range(from, to - from).Where(j => !frames[j].Silent).ToList();
            var profile = Profile(frames, sounding);
            var chord = sounding.Count == 0 ? ChordResult.NoChord : _classifier.Classify(profile);
            var segment = new NoteSegment(start, end, SegmentNotes(profile), profile.BassPitchClass is { } b ? 36 + b : null);

            if (chords.Count > 0 && chords[^1].Chord.Name == chord.Name && Math.Abs(chords[^1].EndSeconds - start) < 1e-9)
                chords[^1] = chords[^1] with { EndSeconds = end, Segment = chords[^1].Segment with { EndSeconds = end } };
            else
                chords.Add(new DetectedChord(start, end, chord, segment));
        }
        return new ChordAnalysis([], chords, durationSeconds);
    }

    /// <summary>Mean chroma of the frames; the bass is the pitch class most often found as bass.</summary>
    private static PitchClassProfile Profile(List<Frame> frames, List<int> indices)
    {
        var w = new double[12];
        var bassVotes = new int[12];
        foreach (var j in indices)
        {
            double max = frames[j].Chroma.Max();
            if (max <= 0) continue;
            for (int pc = 0; pc < 12; pc++) w[pc] += frames[j].Chroma[pc] / max;   // every frame counts equally
            if (frames[j].BassPitch is { } bass) bassVotes[PitchClasses.Of(bass)]++;
        }
        int? bassPc = bassVotes.Max() > 0 ? Array.IndexOf(bassVotes, bassVotes.Max()) : null;
        return new PitchClassProfile(w, bassPc);
    }

    /// <summary>The 12 weights as notes in octave 2–3, so that the UI and debugging can show them.</summary>
    private static IReadOnlyList<SegmentNote> SegmentNotes(PitchClassProfile profile) =>
        Enumerable.Range(0, 12)
            .Where(pc => profile[pc] > 0)
            .Select(pc => new SegmentNote(48 + pc, profile[pc], 1.0))
            .ToList();
}
