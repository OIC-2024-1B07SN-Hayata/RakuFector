using ChordDetection.Core;
using ChordDetection.Core.Models;
using ChordDetection.Infrastructure.Audio;
using static ChordDetection.Core.BasicPitchConstants;

namespace ChordDetection.Infrastructure.BasicPitch;

/// <summary>
/// Streaming form of <see cref="BasicPitchWindowing"/>: a 2-second window is run as soon as all of its
/// samples have arrived, and its kept frames are appended. Window positions and contents are the same
/// as for the whole audio, so after <see cref="Complete"/> the result equals SplitIntoWindows + Unwrap.
/// </summary>
internal sealed class BasicPitchStream : IPitchStream
{
    private const int Pad = OverlapSamples / 2;
    private const int KeptFrom = OverlapFrames / 2;
    private const int KeptTo = FramesPerWindow - OverlapFrames / 2;

    private readonly Func<float[], (float[] Note, float[] Onset)> _runWindow;
    private readonly FloatBuffer _audio = new();   // samples from index _audioStart on
    private readonly FloatBuffer _notes = new();
    private readonly FloatBuffer _onsets = new();
    private long _audioStart;
    private long _totalSamples;
    private int _nextWindow;
    private bool _completed;

    /// <param name="runWindow">Runs the model on one window of <see cref="WindowSamples"/> samples.</param>
    public BasicPitchStream(Func<float[], (float[] Note, float[] Onset)> runWindow)
    {
        _runWindow = runWindow;
    }

    public int Append(ReadOnlySpan<float> monoAudio, CancellationToken cancellationToken = default)
    {
        if (_completed) throw new InvalidOperationException("The stream is already completed.");
        _audio.Append(monoAudio);
        _totalSamples += monoAudio.Length;

        int before = _notes.Count;
        // Window k reads unpadded samples [k * hop - pad, k * hop - pad + WindowSamples).
        while (WindowStart(_nextWindow) + WindowSamples <= _totalSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunNextWindow();
        }
        return (_notes.Count - before) / NoteBins;
    }

    public void Complete(CancellationToken cancellationToken = default)
    {
        if (_completed) return;
        _completed = true;
        if (_totalSamples == 0) return;

        long paddedLength = _totalSamples + Pad;
        while ((long)_nextWindow * WindowHopSamples < paddedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunNextWindow();
        }

        // Same trimming as BasicPitchWindowing.Unwrap.
        int wanted = (int)((double)_totalSamples / WindowHopSamples * (FramesPerWindow - OverlapFrames));
        int frames = Math.Min(_notes.Count / NoteBins, wanted);
        Trim(_notes, frames * NoteBins);
        Trim(_onsets, frames * NoteBins);
    }

    public NoteActivations Activations => new(_notes.Span.ToArray(), _onsets.Span.ToArray());

    public int FrameCount => _notes.Count / NoteBins;

    public NoteActivations ActivationsFrom(int startFrame)
    {
        int from = Math.Clamp(startFrame, 0, FrameCount) * NoteBins;
        return new(_notes.Span[from..].ToArray(), _onsets.Span[from..].ToArray());
    }

    private static long WindowStart(int window) => (long)window * WindowHopSamples - Pad;

    private void RunNextWindow()
    {
        long start = WindowStart(_nextWindow);
        var window = new float[WindowSamples];
        for (int i = 0; i < WindowSamples; i++)
        {
            long src = start + i;
            if (src >= _totalSamples) break;
            if (src >= 0) window[i] = _audio[(int)(src - _audioStart)];
        }

        var (note, onset) = _runWindow(window);
        if (note.Length != FramesPerWindow * NoteBins || onset.Length != FramesPerWindow * NoteBins)
            throw new InvalidOperationException($"Each window must have {FramesPerWindow * NoteBins} values.");
        _notes.Append(note.AsSpan(KeptFrom * NoteBins, (KeptTo - KeptFrom) * NoteBins));
        _onsets.Append(onset.AsSpan(KeptFrom * NoteBins, (KeptTo - KeptFrom) * NoteBins));
        _nextWindow++;

        // Samples before the next window are no longer needed.
        long keepFrom = Math.Max(0, WindowStart(_nextWindow));
        int drop = (int)Math.Min(_audio.Count, keepFrom - _audioStart);
        if (drop > 0)
        {
            _audio.RemoveFront(drop);
            _audioStart += drop;
        }
    }

    private static void Trim(FloatBuffer buffer, int count)
    {
        if (buffer.Count > count) buffer.Truncate(count);
    }
}
