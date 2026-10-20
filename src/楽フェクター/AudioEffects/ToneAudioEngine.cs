using System;
using System.Threading;

namespace FXForge.AudioEffects;

/// <summary>音声スレッド専用。UIには触れず、音作り→8倍FIRダウンサンプル→全体音量。</summary>
public sealed class ToneAudioEngine
{
    private readonly DriveStage _a = new(), _b = new();
    private readonly AmpStage _amp = new();
    private readonly Oversampler8x _resampler = new();
    private ToneSettings _settings = ToneSettings.Default;
    private int _rate;
    private double _master = .65, _masterSmooth, _pathMix, _pathSmooth;

    public void SetSettings(ToneSettings settings) =>
        Volatile.Write(ref _settings, settings ?? throw new ArgumentNullException(nameof(settings)));

    public void Process(float[] samples, int sampleRate)
    {
        if (sampleRate < 8000 || sampleRate > 192000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var settings = Volatile.Read(ref _settings);
        double targetMaster = DriveStage.Unit(settings.OutputLevel);
        double targetPathMix = settings.A.Enabled || settings.B.Enabled || settings.Amp.Enabled ? 1 : 0;
        if (_rate != sampleRate)
        {
            Reset(); _rate = sampleRate; _master = targetMaster; _pathMix = targetPathMix;
            _masterSmooth = 1 - Math.Exp(-1 / (.015 * sampleRate));
            _pathSmooth = 1 - Math.Exp(-1 / (.010 * sampleRate));
        }
        double rate = sampleRate * Oversampler8x.Factor;
        _a.Configure(settings.A, rate); _b.Configure(settings.B, rate); _amp.Configure(settings.Amp, rate);
        var highRateSamples = _resampler.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            double input = float.IsFinite(samples[i]) ? samples[i] : 0;
            _master += (targetMaster - _master) * _masterSmooth;
            _pathMix += (targetPathMix - _pathMix) * _pathSmooth;
            // 原音に切り替えたときも内部の時刻を進め、古いフィルター状態の再生を防ぎます。
            _resampler.Up(input);
            for (int sub = 0; sub < Oversampler8x.Factor; sub++)
            {
                double x = highRateSamples[sub];
                x = settings.ReverseDrives ? _a.Process(_b.Process(x)) : _b.Process(_a.Process(x));
                highRateSamples[sub] = _amp.Process(x);
            }
            double wet = _resampler.Down();
            double output = (input + (wet - input) * _pathMix) * _master;
            samples[i] = double.IsFinite(output) ? (float)SafetyCeiling(output) : 0;
        }
    }

    private static double SafetyCeiling(double input)
    {
        // 最終段で全音色をハードクリップしない。通常音量では透明、過大なピークだけ丸める。
        const double knee = .85, ceiling = .98;
        double magnitude = Math.Abs(input);
        if (magnitude <= knee) return input;
        return Math.CopySign(knee + (ceiling - knee) * Math.Tanh((magnitude - knee) / (ceiling - knee)), input);
    }

    public void Reset()
    {
        _a.Reset(); _b.Reset(); _amp.Reset(); _resampler.Reset();
        _rate = 0; _pathMix = 0;
    }
}
