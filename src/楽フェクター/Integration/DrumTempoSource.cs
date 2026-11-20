using ChordDetection.App.Services;
using DrumEditor.Views;

namespace FXForge.Integration;

/// <summary>The drum editor's pattern as the app's tempo for chord detection.</summary>
public sealed class DrumTempoSource : ITempoSource
{
    private readonly DrumEditorView _drum;

    public DrumTempoSource(DrumEditorView drum) => _drum = drum;

    public int Bpm => _drum.Bpm;
    public int BeatsPerBar => _drum.BeatsPerBar;

    public bool MetronomeEnabled
    {
        get => _drum.MetronomeEnabled;
        set => _drum.MetronomeEnabled = value;
    }

    public double DrumVolume
    {
        get => _drum.DrumVolume;
        set => _drum.DrumVolume = value;
    }

    public double MetronomeVolume
    {
        get => _drum.MetronomeVolume;
        set => _drum.MetronomeVolume = value;
    }

    public event Action? TempoChanged
    {
        add => _drum.TempoChanged += value;
        remove => _drum.TempoChanged -= value;
    }

    public double OutputLatencySeconds => DrumEditorView.OutputLatencySeconds;

    public Task<long?> StartAsync() => _drum.StartTransportAsync();

    public void Stop() => _drum.StopTransport();
}
