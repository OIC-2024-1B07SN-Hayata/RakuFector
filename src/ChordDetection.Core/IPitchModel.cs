using ChordDetection.Core.Models;

namespace ChordDetection.Core;

/// <summary>
/// Stage 3 abstraction: audio → note probabilities.
/// Implemented by Infrastructure (Basic Pitch ONNX), so Core stays free of ONNX/NAudio.
/// </summary>
public interface IPitchModel : IDisposable
{
    /// <param name="monoAudio">Mono samples at <see cref="BasicPitchConstants.SampleRate"/>.</param>
    NoteActivations Predict(float[] monoAudio, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
