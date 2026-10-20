using System;

namespace FXForge.AudioEffects;

// SD-1は前版RAT程度、RATはさらに強い歪みと中高域のざらつき。独自の音色設計です。
internal sealed class DriveStage
{
    private readonly Biquad _inputCut = new(), _inputLow = new(), _gritSplit = new();
    private readonly Biquad _tone = new(), _mid = new(), _body = new(), _dc = new();
    private readonly AntialiasedRatClipper _ratClipper = new(), _gritClipper = new();
    private DriveSettings? _settings;
    private double _rate, _gain = 1, _gainTarget = 1, _level = 1, _levelTarget = 1;
    private double _mix, _mixTarget, _smooth;
    private DriveKind _kind;

    public void Configure(DriveSettings settings, double rate)
    {
        if (_settings == settings && _rate == rate) return;
        double drive = Unit(settings.Drive);
        if (_settings is null || _kind != settings.Kind || _rate != rate ||
            (settings.Enabled && !_settings.Enabled && _mix < 1e-8)) Reset();
        _kind = settings.Kind;
        _mixTarget = settings.Enabled ? 1 : 0;
        _smooth = 1 - Math.Exp(-1 / (.008 * rate));
        bool sd1 = _kind == DriveKind.Sd1;
        _levelTarget = (sd1 ? DriveVoicing.Sd1.LevelScale : DriveVoicing.Rat.LevelScale) * Unit(settings.Level);
        _gainTarget = sd1
            ? DriveVoicing.Sd1.GainMinimum + DriveVoicing.Sd1.GainRange * drive * drive
            : DriveVoicing.Rat.GainMinimum + DriveVoicing.Rat.GainRange * drive * drive;
        _inputCut.HighPass(rate, sd1 ? DriveVoicing.Sd1.InputHighPassHz : DriveVoicing.Rat.InputHighPassHz);
        _inputLow.LowPass(rate, sd1 ? DriveVoicing.Sd1.InputLowPassHz : DriveVoicing.Rat.InputLowPassHz);
        _gritSplit.LowPass(rate, DriveVoicing.Rat.GritSplitHz);
        _dc.HighPass(rate, 18);
        _mid.Peak(rate, sd1 ? DriveVoicing.Sd1.MidHz : DriveVoicing.Rat.MidHz,
            sd1 ? DriveVoicing.Sd1.MidDb : DriveVoicing.Rat.MidDb, .75);
        _body.Shelf(rate, sd1 ? DriveVoicing.Sd1.BodyHz : DriveVoicing.Rat.BodyHz,
            sd1 ? DriveVoicing.Sd1.BodyDb : DriveVoicing.Rat.BodyDb, false);
        double cutoff = sd1
            ? DriveVoicing.Sd1.ToneMinHz * Math.Pow(DriveVoicing.Sd1.ToneMaxHz / DriveVoicing.Sd1.ToneMinHz, Unit(settings.Tone))
            : DriveVoicing.Rat.FilterMinHz * Math.Pow(DriveVoicing.Rat.FilterMaxHz / DriveVoicing.Rat.FilterMinHz, 1 - Unit(settings.Tone));
        _tone.LowPass(rate, cutoff);
        _settings = settings;
        _rate = rate;
    }

    public double Process(double input)
    {
        _gain += (_gainTarget - _gain) * _smooth;
        _level += (_levelTarget - _level) * _smooth;
        _mix += (_mixTarget - _mix) * _smooth;
        // 完全OFFの段では重いフィルターや歪み計算を省く。再ON時には上で状態をリセット。
        if (_mixTarget == 0 && _mix < 1e-8) return input;
        double clean = _inputLow.Process(_inputCut.Process(input));
        double shaped;
        if (_kind == DriveKind.Sd1)
        {
            double driven = DriveCurves.Sd1(clean * _gain);
            shaped = _body.Process(driven * (1 - DriveVoicing.Sd1.CleanBlend) + clean * DriveVoicing.Sd1.CleanBlend);
        }
        else
        {
            double driven = _ratClipper.Process(clean * _gain);
            double upper = clean - _gritSplit.Process(clean);
            double grit = _gritClipper.Process(upper * _gain * DriveVoicing.Rat.GritGain);
            shaped = _body.Process((driven + grit * DriveVoicing.Rat.GritMix) / (1 + DriveVoicing.Rat.GritMix));
        }
        double wet = _tone.Process(_mid.Process(_dc.Process(shaped))) * _level;
        return input + (wet - input) * _mix;
    }

    public void Reset()
    {
        _inputCut.Reset(); _inputLow.Reset(); _gritSplit.Reset();
        _tone.Reset(); _mid.Reset(); _body.Reset(); _dc.Reset();
        _ratClipper.Reset(); _gritClipper.Reset();
        _gain = _level = 1; _mix = 0;
    }
    internal static double Unit(double x) => double.IsFinite(x) ? Math.Clamp(x, 0, 1) : .5;
}
