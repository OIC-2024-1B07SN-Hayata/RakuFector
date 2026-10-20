using System;

namespace FXForge.AudioEffects;

// 汎用のアンプ風モデル。Gain、3バンドEQ、Level、高域整形。
internal sealed class AmpStage
{
    private readonly Biquad _cut = new(), _bass = new(), _middle = new(), _treble = new(), _highCut = new(), _highCut2 = new();
    private AmpSettings? _settings;
    private double _rate, _gain = 1, _gainTarget = 1, _level = 1, _levelTarget = 1;
    private double _mix, _mixTarget, _smooth;

    public void Configure(AmpSettings settings, double rate)
    {
        if (_settings == settings && _rate == rate) return;
        if (_rate != rate || (settings.Enabled && _settings is { Enabled: false } && _mix < 1e-8)) Reset();
        double gain = DriveStage.Unit(settings.Gain);
        _gainTarget = 1 + 12 * gain * gain;
        _levelTarget = 1.5 * DriveStage.Unit(settings.Level);
        _mixTarget = settings.Enabled ? 1 : 0;
        _smooth = 1 - Math.Exp(-1 / (.008 * rate));
        _cut.HighPass(rate, 45);
        _bass.Shelf(rate, 130, (DriveStage.Unit(settings.Bass) - .5) * 24, false);
        _middle.Peak(rate, 800, (DriveStage.Unit(settings.Middle) - .5) * 24, .8);
        _treble.Shelf(rate, 3000, (DriveStage.Unit(settings.Treble) - .5) * 24, true);
        // 簡易的な出力整形。IRキャビネットではありません。
        _highCut.LowPass(rate, 6500, .5411961001);
        _highCut2.LowPass(rate, 6500, 1.306562965);
        _settings = settings; _rate = rate;
    }

    public double Process(double input)
    {
        _gain += (_gainTarget - _gain) * _smooth;
        _level += (_levelTarget - _level) * _smooth;
        _mix += (_mixTarget - _mix) * _smooth;
        if (_mixTarget == 0 && _mix < 1e-8) return input;
        double x = .95 * Math.Tanh(_cut.Process(input) * _gain / .95);
        x = _treble.Process(_middle.Process(_bass.Process(x)));
        double wet = _highCut2.Process(_highCut.Process(x)) * _level;
        return input + (wet - input) * _mix;
    }

    public void Reset()
    {
        _cut.Reset(); _bass.Reset(); _middle.Reset(); _treble.Reset(); _highCut.Reset(); _highCut2.Reset();
        _gain = _level = 1; _mix = 0;
    }
}
