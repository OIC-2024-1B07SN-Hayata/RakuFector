using System;

namespace FXForge.AudioEffects;

/// <summary>
/// SD-1 / RATは既存DriveStageを使用。
/// TS9 / BD-2 / DS-1は本クラスで処理する。
/// </summary>
internal sealed class SelectableDriveStage
{
    private readonly DriveStage _existing = new();

    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _inputLowPass = new();
    private readonly Biquad _preMid = new();
    private readonly Biquad _betweenStages = new();

    private readonly Biquad _body = new();
    private readonly Biquad _postMid = new();
    private readonly Biquad _dc = new();
    private readonly Biquad _toneLowPass = new();

    // BD-2 / DS-1の高域調整。
    private readonly Biquad _presence = new();

    // DS-1用の並列Tone。
    private readonly Biquad _dsToneLow = new();
    private readonly Biquad _dsToneHigh = new();
    private readonly Biquad _dsOutputLowPass = new();

    private DriveSettings? _settings;
    private DriveKind _kind;

    private double _rate;
    private double _smooth;

    private double _gain = 1;
    private double _gainTarget = 1;

    private double _secondGain = 1;
    private double _secondGainTarget = 1;

    private double _level;
    private double _levelTarget;

    private double _mix;
    private double _mixTarget;

    private double _toneMix = .5;
    private double _toneMixTarget = .5;

    private static bool IsExisting(DriveKind kind)
    {
        return kind is DriveKind.Sd1 or DriveKind.Rat;
    }

    public void Configure(DriveSettings settings, double rate)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(rate) ||
            rate < 8000 ||
            rate > 1536000)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        if (settings.Kind is not (
            DriveKind.Sd1 or
            DriveKind.Rat or
            DriveKind.Ts9 or
            DriveKind.Bd2 or
            DriveKind.Ds1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "未対応の歪み種類です。");
        }

        if (_settings == settings && _rate == rate)
            return;

        bool first = _settings is null;
        bool changedKind = !first && _kind != settings.Kind;
        bool changedRate = _rate != rate;

        bool enabledFromOff =
            !first &&
            !IsExisting(settings.Kind) &&
            settings.Enabled &&
            !_settings!.Enabled &&
            _mix < 1e-8;

        if (first ||
            changedKind ||
            changedRate ||
            enabledFromOff)
        {
            Reset();
        }

        _kind = settings.Kind;

        // SD-1 / RATは既存の処理をそのまま使用。
        if (IsExisting(_kind))
        {
            _existing.Configure(settings, rate);

            _settings = settings;
            _rate = rate;

            return;
        }

        double drive = DriveStage.Unit(settings.Drive);
        double tone = DriveStage.Unit(settings.Tone);
        double level = DriveStage.Unit(settings.Level);

        _smooth = 1 - Math.Exp(-1 / (.008 * rate));
        _mixTarget = settings.Enabled ? 1 : 0;

        _secondGainTarget = 1;
        _toneMixTarget = .5;

        // 既存の音量基準を維持。
        double referenceLevel = DriveVoicing.Rat.LevelScale;

        _dc.HighPass(rate, 18);

        switch (_kind)
        {
            case DriveKind.Ts9:
                // ====================================
                // TS9
                //
                // 旧：1.5 + 28 * drive²
                // 新：1.5 + 18 * drive + 48 * drive²
                //
                // 低いDriveでも変化を出し、
                // 中～高Driveの増幅も強める。
                // ====================================

                _gainTarget =
                    1.5 +
                    18.0 * drive +
                    48.0 * drive * drive;

                _levelTarget =
                    referenceLevel * .80 * level;

                _inputHighPass.HighPass(rate, 200);
                _inputLowPass.LowPass(rate, 7500);

                _preMid.Peak(rate, 720, 2.5, .8);

                _body.Shelf(
                    rate,
                    180,
                    -1.5,
                    false);

                _postMid.Peak(
                    rate,
                    850,
                    2.0,
                    .75);

                _toneLowPass.LowPass(
                    rate,
                    LogFrequency(1500, 6500, tone));
                break;

            case DriveKind.Bd2:
                // ====================================
                // BD-2
                //
                // ゲインは維持。
                // 段間と最終Toneの高域制限を緩め、
                // 歪み後に高域を持ち上げる。
                // ====================================

                _gainTarget =
                    1 + 60 * drive * drive;

                _secondGainTarget =
                    1 + 3 * drive;

                _levelTarget =
                    referenceLevel * .65 * level;

                _inputHighPass.HighPass(rate, 65);
                _inputLowPass.LowPass(rate, 11000);

                _preMid.Peak(
                    rate,
                    1100,
                    .5,
                    .7);

                // 8500Hz → 10000Hz。
                _betweenStages.LowPass(rate, 10000);

                _body.Shelf(
                    rate,
                    140,
                    .5,
                    false);

                _postMid.Peak(
                    rate,
                    900,
                    -.8,
                    .8);

                // 旧：1800～10000Hz
                // 新：3000～12000Hz
                _toneLowPass.LowPass(
                    rate,
                    LogFrequency(3000, 12000, tone));

                // カッティングの輪郭・弦の高域を強調。
                _presence.Shelf(
                    rate,
                    3000,
                    3.5,
                    true);
                break;

            case DriveKind.Ds1:
                // ====================================
                // DS-1
                //
                // 歪み量は維持。
                // 後段の帯域と高域EQで
                // シャリッとした質感を増やす。
                // ====================================

                _gainTarget =
                    3 + 85 * drive * drive;

                _levelTarget =
                    referenceLevel * 1.20 * level;

                _inputHighPass.HighPass(rate, 100);
                _inputLowPass.LowPass(rate, 9500);

                _preMid.Peak(
                    rate,
                    1000,
                    3.0,
                    .85);

                _body.Shelf(
                    rate,
                    140,
                    .8,
                    false);

                _postMid.Peak(
                    rate,
                    750,
                    -3.0,
                    .8);

                _dsToneLow.LowPass(rate, 750);
                _dsToneHigh.HighPass(rate, 1600);

                // 7000Hz → 9000Hz。
                _dsOutputLowPass.LowPass(rate, 9000);

                _presence.Shelf(
                    rate,
                    3200,
                    3.0,
                    true);

                _toneMixTarget = .10 + .80 * tone;
                break;
        }

        _settings = settings;
        _rate = rate;
    }

    public double Process(double input)
    {
        if (!double.IsFinite(input))
            return 0;

        if (_settings is null)
            return input;

        // 追加EQなどはSD-1 / RATを通さない。
        if (IsExisting(_kind))
            return _existing.Process(input);

        _gain +=
            (_gainTarget - _gain) * _smooth;

        _secondGain +=
            (_secondGainTarget - _secondGain) * _smooth;

        _level +=
            (_levelTarget - _level) * _smooth;

        _mix +=
            (_mixTarget - _mix) * _smooth;

        _toneMix +=
            (_toneMixTarget - _toneMix) * _smooth;

        if (_mixTarget == 0 && _mix < 1e-8)
            return input;

        double clean = _inputLowPass.Process(
            _inputHighPass.Process(input));

        double pre = _preMid.Process(clean);

        double shaped;

        switch (_kind)
        {
            case DriveKind.Ts9:
                {
                    double clipped = SymmetricSoftClip(
                        pre * _gain,
                        .62);

                    // 原音混合率は維持。
                    shaped =
                        clipped * .75 +
                        clean * .25;
                    break;
                }

            case DriveKind.Bd2:
                {
                    double first = AsymmetricSoftClip(
                        pre * _gain,
                        .86,
                        .74);

                    double secondInput =
                        _betweenStages.Process(first);

                    const double bias = .025;

                    double second =
                        AsymmetricSoftClip(
                            secondInput * _secondGain + bias,
                            .78,
                            .66)
                        - AsymmetricSoftClip(
                            bias,
                            .78,
                            .66);

                    // 歪み段と原音の比率は維持。
                    shaped =
                        first * .42 +
                        second * .50 +
                        clean * .08;
                    break;
                }

            case DriveKind.Ds1:
                shaped = SoftKnee(
                    pre * _gain,
                    .48,
                    .025);
                break;

            default:
                return input;
        }

        double wet = _postMid.Process(
            _dc.Process(
                _body.Process(shaped)));

        if (_kind == DriveKind.Ds1)
        {
            double low = _dsToneLow.Process(wet);
            double high = _dsToneHigh.Process(wet);

            wet =
                low * (1 - _toneMix) +
                high * _toneMix;

            wet = _dsOutputLowPass.Process(wet);
        }
        else
        {
            wet = _toneLowPass.Process(wet);
        }

        // BD-2 / DS-1のみ高域EQを適用。
        if (_kind is DriveKind.Bd2 or DriveKind.Ds1)
            wet = _presence.Process(wet);

        wet *= _level;

        return input + (wet - input) * _mix;
    }

    public void Reset()
    {
        _existing.Reset();

        _inputHighPass.Reset();
        _inputLowPass.Reset();
        _preMid.Reset();
        _betweenStages.Reset();

        _body.Reset();
        _postMid.Reset();
        _dc.Reset();
        _toneLowPass.Reset();
        _presence.Reset();

        _dsToneLow.Reset();
        _dsToneHigh.Reset();
        _dsOutputLowPass.Reset();

        _gain = _gainTarget = 1;
        _secondGain = _secondGainTarget = 1;

        _level = _levelTarget = 0;
        _mix = _mixTarget = 0;
        _toneMix = _toneMixTarget = .5;

        _settings = null;
        _rate = 0;
    }

    private static double LogFrequency(
        double minimum,
        double maximum,
        double amount)
    {
        return minimum * Math.Pow(
            maximum / minimum,
            amount);
    }

    private static double SymmetricSoftClip(
        double input,
        double ceiling)
    {
        return ceiling * Math.Tanh(input / ceiling);
    }

    private static double AsymmetricSoftClip(
        double input,
        double positiveCeiling,
        double negativeCeiling)
    {
        double ceiling = input >= 0
            ? positiveCeiling
            : negativeCeiling;

        return ceiling * Math.Tanh(input / ceiling);
    }

    private static double SoftKnee(
        double input,
        double ceiling,
        double knee)
    {
        double magnitude = Math.Abs(input);
        double start = ceiling - knee;

        double result = magnitude <= start
            ? magnitude
            : start + knee * Math.Tanh(
                (magnitude - start) / knee);

        return Math.CopySign(result, input);
    }
}