using ChordDetection.Core.Models;

namespace ChordDetection.Core;

/// <summary>A pitch model that can also be fed audio piece by piece (streaming analysis).</summary>
public interface IStreamingPitchModel : IPitchModel
{
    IPitchStream CreateStream();
}

/// <summary>
/// Stage 3 fed piece by piece. The model output for the audio received so far grows as pieces arrive;
/// after <see cref="Complete"/> it is identical to <see cref="IPitchModel.Predict"/> on the whole audio.
/// </summary>
public interface IPitchStream
{
    /// <summary>Adds mono samples at <see cref="BasicPitchConstants.SampleRate"/>; returns the number of new frames.</summary>
    int Append(ReadOnlySpan<float> monoAudio, CancellationToken cancellationToken = default);

    /// <summary>Signals the end of the audio and processes the rest.</summary>
    void Complete(CancellationToken cancellationToken = default);

    /// <summary>Model output so far (a copy).</summary>
    NoteActivations Activations { get; }

    /// <summary>Number of frames in <see cref="Activations"/>.</summary>
    int FrameCount => Activations.FrameCount;

    /// <summary>Model output from <paramref name="startFrame"/> on (a copy); cheaper than the whole output for long streams.</summary>
    NoteActivations ActivationsFrom(int startFrame)
    {
        var all = Activations;
        int from = Math.Clamp(startFrame, 0, all.FrameCount) * BasicPitchConstants.NoteBins;
        return new NoteActivations(all.Frames[from..], all.Onsets[from..]);
    }
}
