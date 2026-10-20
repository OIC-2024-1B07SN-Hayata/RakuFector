using System.Diagnostics;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;
using ChordDetection.Core.Notes;
using ChordDetection.Core.Segmentation;

namespace ChordDetection.Core;

/// <summary>Result of one analysis. Chords are stored at the detailed level.</summary>
public sealed record ChordAnalysis(
    IReadOnlyList<NoteEvent> Notes,
    IReadOnlyList<DetectedChord> Chords,
    double DurationSeconds);

/// <summary>
/// Runs stages 3–6: pitch model → note decoding → segmentation → classification.
/// Stage 7 (display simplification) is applied by the UI with <see cref="ChordSimplifier"/>.
/// </summary>
public sealed class ChordDetectionService
{
    private readonly IPitchModel _model;
    private readonly NoteDecoder _decoder;
    private readonly NoteSegmenter _segmenter;
    private readonly IChordClassifier _classifier;
    private readonly HarmonicFilter? _harmonics;

    /// <param name="harmonics">Overtone suppression before classification; defaults to <see cref="HarmonicFilter"/> with default options.</param>
    /// <param name="disableHarmonicFilter">True to classify the raw notes (no overtone suppression).</param>
    public ChordDetectionService(
        IPitchModel model,
        NoteDecoder? decoder = null,
        NoteSegmenter? segmenter = null,
        IChordClassifier? classifier = null,
        HarmonicFilter? harmonics = null,
        bool disableHarmonicFilter = false)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _decoder = decoder ?? new NoteDecoder();
        _segmenter = segmenter ?? new NoteSegmenter();
        _classifier = classifier ?? new RuleBasedChordClassifier();
        _harmonics = disableHarmonicFilter ? null : harmonics ?? new HarmonicFilter();
    }

    /// <param name="monoAudio">Preprocessed audio (mono, 22,050 Hz).</param>
    public ChordAnalysis Analyze(float[] monoAudio, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monoAudio);
        var activations = _model.Predict(monoAudio, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return AnalyzeActivations(activations, (double)monoAudio.Length / BasicPitchConstants.SampleRate);
    }

    /// <summary>
    /// Streaming version of <see cref="Analyze"/>: mono pieces (22,050 Hz) are fed to the model as they arrive,
    /// and the chords found so far are reported every <paramref name="updateInterval"/>.
    /// The last part of an intermediate result may still change; the returned final result is
    /// identical to <see cref="Analyze"/> on the whole audio.
    /// Intermediate results only re-analyse the last <see cref="TailWindowSeconds"/>, so their cost does not grow
    /// with the length of the performance (see <see cref="UpdateTail"/>).
    /// </summary>
    public ChordAnalysis AnalyzeStreaming(
        IEnumerable<float[]> monoPieces,
        IProgress<ChordAnalysis>? updates = null,
        TimeSpan? updateInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monoPieces);
        if (_model is not IStreamingPitchModel streamingModel)
            return Analyze(monoPieces.SelectMany(p => p).ToArray(), null, cancellationToken);

        var stream = streamingModel.CreateStream();
        var interval = updateInterval ?? TimeSpan.FromSeconds(0.5);
        var clock = Stopwatch.StartNew();
        var lastUpdate = -interval;
        long samples = 0;
        bool changed = false;
        ChordAnalysis? current = null;

        foreach (var piece in monoPieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples += piece.Length;
            changed |= stream.Append(piece, cancellationToken) > 0;
            if (updates is null || !changed || clock.Elapsed - lastUpdate < interval) continue;

            current = UpdateTail(current, stream);
            updates.Report(current);
            lastUpdate = clock.Elapsed;
            changed = false;
        }

        stream.Complete(cancellationToken);
        return AnalyzeActivations(stream.Activations, (double)samples / BasicPitchConstants.SampleRate);
    }

    /// <summary>Length of model output decoded again for each intermediate streaming result.</summary>
    public const double TailWindowSeconds = 12;

    /// <summary>
    /// Start of the tail window that is only context: chords starting there are kept from the previous result.
    /// Longer than <see cref="NoteSegmenterOptions.RecentNoteSeconds"/> so that notes ringing on from the previous chord
    /// are seen with their real start.
    /// </summary>
    public const double TailContextSeconds = 4;

    /// <summary>
    /// Intermediate streaming result. Chords that started more than about
    /// <see cref="TailWindowSeconds"/> − <see cref="TailContextSeconds"/> ago are kept from <paramref name="previous"/>;
    /// only the tail is decoded and segmented again, so each update costs the same however long the performance is.
    /// The final result is still computed on the whole audio.
    /// </summary>
    private ChordAnalysis UpdateTail(ChordAnalysis? previous, IPitchStream stream)
    {
        int frameCount = stream.FrameCount;
        double duration = (double)frameCount * BasicPitchConstants.FftHop / BasicPitchConstants.SampleRate;
        int startFrame = frameCount - (int)(TailWindowSeconds * BasicPitchConstants.FramesPerSecond);
        if (previous is null || startFrame <= 0)
            return AnalyzeActivations(stream.Activations, duration);

        double cut = BasicPitchConstants.FrameToSeconds(startFrame) + TailContextSeconds;
        var tailNotes = _decoder.Decode(stream.ActivationsFrom(startFrame), startFrame);
        var tail = AnalyzeNotes(tailNotes, duration);

        var chords = previous.Chords.Where(c => c.StartSeconds < cut).ToList();
        var fresh = tail.Chords.Where(c => c.StartSeconds >= cut).ToList();

        // The chord sounding at the cut may have grown since the previous update.
        var across = tail.Chords.FirstOrDefault(c => c.StartSeconds < cut && c.EndSeconds > cut);
        if (chords.Count > 0 && across is not null && across.Chord.Name == chords[^1].Chord.Name)
        {
            double end = fresh.Count > 0 ? Math.Min(across.EndSeconds, fresh[0].StartSeconds) : across.EndSeconds;
            if (end > chords[^1].EndSeconds) chords[^1] = chords[^1] with { EndSeconds = end };
        }
        chords.AddRange(fresh);

        var notes = previous.Notes.Where(n => n.StartSeconds < cut)
            .Concat(tailNotes.Where(n => n.StartSeconds >= cut))
            .ToList();
        return new ChordAnalysis(notes, chords, duration);
    }

    /// <summary>
    /// Stages 4–6 on model output. Streaming analysis uses it for the first seconds and the final result.
    /// </summary>
    public ChordAnalysis AnalyzeActivations(NoteActivations activations, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(activations);
        return AnalyzeNotes(_decoder.Decode(activations), durationSeconds);
    }

    /// <summary>Stages 5–6 only. Useful for tests and for re-analysing an exported note list.</summary>
    public ChordAnalysis AnalyzeNotes(IReadOnlyList<NoteEvent> notes, double durationSeconds)
    {
        var chords = _segmenter.Segment(notes)
            .Select(s => new DetectedChord(s.StartSeconds, s.EndSeconds,
                _classifier.Classify(PitchClassProfile.FromSegment(s, _harmonics)), s))
            .ToList();
        return new ChordAnalysis(notes, chords, durationSeconds);
    }
}
