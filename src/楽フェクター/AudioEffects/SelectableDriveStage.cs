using System;

namespace FXForge.AudioEffects;

/// <summary>
/// SD-1 / RATは既存DriveStageを使用。
/// TS9 / BD-2 / DS-1は本クラスで処理する。
/// 音声スレッドからのみ使用する。
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

    private readonly Biquad _presence = new();

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

    // BD-2専用。
    // Toneが高いときに第2段の割合を下げ、
    // 第1段と原音の割合を増やす。
    private double _bdTone;
    private double _bdToneTarget;

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

        // SD-1 / RATの確定済み処理は変更しない。
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
        _bdToneTarget = 0;

        double referenceLevel = DriveVoicing.Rat.LevelScale;

        _dc.HighPass(rate, 18);

        switch (_kind)
        {
            case DriveKind.Ts9:
                {
                    // TS9：現在の設定を維持。
                    _gainTarget =
                        1.5 +
                        18.0 * drive +
                        48.0 * drive * drive;

                    _levelTarget =
                        referenceLevel * .80 * level;

                    _inputHighPass.HighPass(rate, 200);
                    _inputLowPass.LowPass(rate, 7500);

                    _preMid.Peak(
                        rate,
                        720,
                        2.5,
                        .8);

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
                }

            case DriveKind.Bd2:
                {
                    // BD-2：高Tone時の潰れを抑える。
                    //
                    // 高域・プレゼンスの追従は維持するが、
                    // 増幅量を抑えて後段への入力過多を軽減。

                    double highDb =
                        4.0 + 4.0 * tone;

                    double presenceDb =
                        2.0 + 2.5 * tone;

                    // 旧：最大15％増加。
                    // 新：最大3％に抑え、Toneで歪みすぎないようにする。
                    double toneDriveBoost =
                        1.0 + .03 * tone;

                    // Toneによる音量増加は最大2％に抑える。
                    double toneLevelBoost =
                        1.0 + .02 * tone;

                    _gainTarget =
                        (1.0 + 60.0 * drive * drive)
                        * toneDriveBoost;

                    // 高Toneでは第2段への増幅を少し下げる。
                    // Driveによる歪みの調整範囲は残す。
                    _secondGainTarget =
                        (1.0 + 3.0 * drive)
                        * (1.0 - .15 * tone);

                    _levelTarget =
                        referenceLevel
                        * .65
                        * level
                        * toneLevelBoost;

                    _bdToneTarget = tone;

                    _inputHighPass.HighPass(rate, 65);
                    _inputLowPass.LowPass(rate, 14000);

                    _preMid.Peak(
                        rate,
                        1100,
                        .5,
                        .7);

                    _betweenStages.LowPass(rate, 12000);

                    _body.Shelf(
                        rate,
                        140,
                        .5,
                        false);

                    // 高音の張りを支える帯域。
                    _postMid.Peak(
                        rate,
                        2800,
                        presenceDb,
                        .85);

                    _toneLowPass.LowPass(
                        rate,
                        LogFrequency(4500, 14000, tone));

                    _presence.Shelf(
                        rate,
                        2500,
                        highDb,
                        true);

                    break;
                }

            case DriveKind.Ds1:
                {
                    // DS-1：厚みを補う現在の設定を維持。
                    _gainTarget =
                        3.0 +
                        25.0 * drive +
                        160.0 * drive * drive;

                    _levelTarget =
                        referenceLevel * 1.20 * level;

                    _inputHighPass.HighPass(rate, 75);
                    _inputLowPass.LowPass(rate, 9500);

                    _preMid.Peak(
                        rate,
                        1000,
                        3.0,
                        .85);

                    _body.Shelf(
                        rate,
                        220,
                        2.0,
                        false);

                    _postMid.Peak(
                        rate,
                        750,
                        -1.0,
                        .8);

                    _dsToneLow.LowPass(rate, 1000);
                    _dsToneHigh.HighPass(rate, 1300);

                    _dsOutputLowPass.LowPass(rate, 9000);

                    _presence.Shelf(
                        rate,
                        3200,
                        3.0,
                        true);

                    _toneMixTarget =
                        .10 + .75 * tone;

                    break;
                }
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

        _bdTone +=
            (_bdToneTarget - _bdTone) * _smooth;

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

                    double secondDriven =
                        secondInput * _secondGain * 1.25;

                    double second = SoftKnee(
                        secondDriven,
                        .68,
                        .015);

                    // Tone 0：
                    // 第1段35％ / 第2段55％ / 原音10％
                    //
                    // Tone 100：
                    // 第1段45％ / 第2段40％ / 原音15％
                    //
                    // 高Toneほど第2段の硬い歪みを減らし、
                    // ピッキングの強弱と弦の輪郭を残す。
                    double firstMix =
                        .35 + .10 * _bdTone;

                    double secondMix =
                        .55 - .15 * _bdTone;

                    double cleanMix =
                        .10 + .05 * _bdTone;

                    shaped =
                        first * firstMix +
                        second * secondMix +
                        clean * cleanMix;

                    break;
                }

            case DriveKind.Ds1:
                {
                    shaped = SoftKnee(
                        pre * _gain,
                        .48,
                        .025);

                    break;
                }

            default:
                return input;
        }

        double wet = _postMid.Process(
            _dc.Process(
                _body.Process(shaped)));

        if (_kind == DriveKind.Ds1)
        {
            double low =
                _dsToneLow.Process(wet);

            double high =
                _dsToneHigh.Process(wet);

            wet =
                low * (1 - _toneMix) +
                high * _toneMix;

            wet =
                _dsOutputLowPass.Process(wet);
        }
        else
        {
            wet =
                _toneLowPass.Process(wet);
        }

        if (_kind is DriveKind.Bd2 or DriveKind.Ds1)
        {
            wet =
                _presence.Process(wet);
        }

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

        _bdTone = _bdToneTarget = 0;

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