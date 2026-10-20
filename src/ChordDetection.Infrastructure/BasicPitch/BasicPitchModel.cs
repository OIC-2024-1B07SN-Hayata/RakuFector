using ChordDetection.Core;
using ChordDetection.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ChordDetection.Infrastructure.BasicPitch;

/// <summary>
/// Stage 3: Basic Pitch (ICASSP 2022) inference with ONNX Runtime.
/// Runs the model on 2-second windows and stitches the results.
/// </summary>
public sealed class BasicPitchModel : IStreamingPitchModel
{
    // Tensor names of nmp.onnx (same as basic_pitch/inference.py, Model.predict for ONNX).
    private const string InputName = "serving_default_input_2:0";
    private const string NoteOutputName = "StatefulPartitionedCall:1";
    private const string OnsetOutputName = "StatefulPartitionedCall:2";

    private static readonly int[] InputShape = [1, BasicPitchConstants.WindowSamples, 1];

    private readonly InferenceSession _session;
    private readonly string[] _outputNames = [NoteOutputName, OnsetOutputName];

    public BasicPitchModel(string? modelPath = null)
    {
        modelPath ??= DefaultModelPath;
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Basic Pitch model not found: {modelPath}", modelPath);

        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(modelPath, options);
    }

    /// <summary>Models\nmp.onnx next to the executable.</summary>
    public static string DefaultModelPath => Path.Combine(AppContext.BaseDirectory, "Models", "nmp.onnx");

    public NoteActivations Predict(float[] monoAudio, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monoAudio);
        if (monoAudio.Length == 0) return NoteActivations.Empty;

        var windows = BasicPitchWindowing.SplitIntoWindows(monoAudio);
        var notes = new List<float[]>(windows.Count);
        var onsets = new List<float[]>(windows.Count);

        for (int i = 0; i < windows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (note, onset) = RunWindow(windows[i]);
            notes.Add(note);
            onsets.Add(onset);
            progress?.Report((i + 1) / (double)windows.Count);
        }

        return BasicPitchWindowing.Unwrap(notes, onsets, monoAudio.Length);
    }

    /// <summary>Runs the model window by window as audio arrives (used for streaming analysis).</summary>
    public IPitchStream CreateStream() => new BasicPitchStream(RunWindow);

    private (float[] Note, float[] Onset) RunWindow(float[] window)
    {
        var tensor = new DenseTensor<float>(window, InputShape);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(InputName, tensor) };

        using var results = _session.Run(inputs, _outputNames);
        float[]? note = null, onset = null;
        foreach (var r in results)
        {
            // Output shape [1, 172, 88] → row-major float[172 * 88]
            if (r.Name == NoteOutputName) note = r.AsEnumerable<float>().ToArray();
            else if (r.Name == OnsetOutputName) onset = r.AsEnumerable<float>().ToArray();
        }

        if (note is null || onset is null)
            throw new InvalidOperationException("Basic Pitch model did not return note/onset outputs.");
        return (note, onset);
    }

    public void Dispose() => _session.Dispose();
}
