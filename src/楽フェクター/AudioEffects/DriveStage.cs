
using System;

namespace FXForge.AudioEffects;

internal sealed class DriveStage
{
    // ========================================
    // 共通フィルター
    // ========================================

    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _inputLowPass = new();
    private readonly Biquad _tone = new();
    private readonly Biquad _mid = new();
    private readonly Biquad _body = new();
    private readonly Biquad _dc = new();

    // ========================================
    // RAT メインクリッピング
    // ========================================

    private readonly Biquad _ratPreBass = new();
    private readonly Biquad _ratPreMid = new();

    private readonly AntialiasedRatClipper
        _ratMainClipper = new();

    // ========================================
    // RAT 第2クリッピング
    // ========================================

    private readonly Biquad _ratSecondHighPass = new();
    private readonly Biquad _ratSecondMid = new();
    private readonly Biquad _ratSecondLowPass = new();

    private readonly AntialiasedRatClipper
        _ratSecondClipper = new();

    // ========================================
    // Drive連動EQ
    // ========================================

    private readonly Biquad _ratDriveBassEq = new();
    private readonly Biquad _ratDriveMidEq = new();

    // ========================================
    // 内部状態
    // ========================================

    private DriveSettings? _settings;
    private DriveKind _kind;

    private double _rate;
    private double _smooth;

    private double _gain = 1.0;
    private double _gainTarget = 1.0;

    private double _level = 1.0;
    private double _levelTarget = 1.0;

    private double _mix;
    private double _mixTarget;

    private double _driveEqMix;
    private double _driveEqTarget;

    private double _secondGain = 1.0;
    private double _secondGainTarget = 1.0;

    // 弱い入力音への限定的な増幅補助
    private double _inputEnvelope;
    private double _inputBoost = 1.0;

    private double _envAttack;
    private double _envRelease;

    // ========================================
    // 設定
    // ========================================

    public void Configure(
        DriveSettings settings,
        double rate)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(rate) ||
            rate < 8000 ||
            rate > 1536000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate));
        }

        if (_settings == settings && _rate == rate)
        {
            return;
        }

        bool first = _settings is null;
        bool changedKind =
            !first && _kind != settings.Kind;
        bool changedRate = _rate != rate;

        bool enabledFromOff =
            !first &&
            settings.Enabled &&
            !_settings!.Enabled &&
            _mix < 1e-8;

        if (first || changedKind ||
            changedRate || enabledFromOff)
        {
            Reset();
        }

        _kind = settings.Kind;
        _mixTarget = settings.Enabled ? 1.0 : 0.0;

        _smooth =
            1.0 - Math.Exp(-1.0 / (0.008 * rate));

        bool isSd1 = _kind == DriveKind.Sd1;

        double drive = Unit(settings.Drive);
        double tone = Unit(settings.Tone);

        // ====================================
        // メインGAIN
        // ====================================

        _gainTarget = isSd1
            ? DriveVoicing.Sd1.GainMinimum
              + DriveVoicing.Sd1.GainRange
              * drive * drive
            : DriveVoicing.Rat.GainMinimum
              + DriveVoicing.Rat.GainRange
              * drive * drive;

        // ====================================
        // 出力LEVEL
        // ====================================

        _levelTarget =
            (isSd1
                ? DriveVoicing.Sd1.LevelScale
                : DriveVoicing.Rat.LevelScale)
            * Unit(settings.Level);

        // RAT専用Drive連動設定
        _driveEqTarget = isSd1 ? 0.0 : drive;

        _secondGainTarget =
            DriveVoicing.Rat.SecondStageGainMin
            + DriveVoicing.Rat.SecondStageGainRange
            * drive;

        // ====================================
        // 入力フィルター
        // ====================================

        _inputHighPass.HighPass(
            rate,
            isSd1
                ? DriveVoicing.Sd1.InputHighPassHz
                : DriveVoicing.Rat.InputHighPassHz);

        _inputLowPass.LowPass(
            rate,
            isSd1
                ? DriveVoicing.Sd1.InputLowPassHz
                : DriveVoicing.Rat.InputLowPassHz);

        // ====================================
        // RAT専用の処理
        // ====================================

        if (!isSd1)
        {
            // 歪み前の低域を削る
            _ratPreBass.Shelf(
                rate,
                DriveVoicing.Rat.PreBassHz,
                DriveVoicing.Rat.PreBassDb,
                false);

            // 歪み前の中域を補強
            _ratPreMid.Peak(
                rate,
                DriveVoicing.Rat.PreMidHz,
                DriveVoicing.Rat.PreMidDb,
                DriveVoicing.Rat.PreMidQ);

            // 第2段の前に低域を整理
            _ratSecondHighPass.HighPass(
                rate,
                DriveVoicing.Rat.SecondHighPassHz);

            // 第2段の前の中域
            _ratSecondMid.Peak(
                rate,
                DriveVoicing.Rat.SecondMidHz,
                DriveVoicing.Rat.SecondMidDb,
                DriveVoicing.Rat.SecondMidQ);

            _ratSecondLowPass.LowPass(
                rate,
                DriveVoicing.Rat.SecondLowPassHz);

            // Drive連動の低域EQ
            _ratDriveBassEq.Shelf(
                rate,
                DriveVoicing.Rat.DriveBassHz,
                DriveVoicing.Rat.DriveBassBoostDb,
                false);

            // Drive連動の中域EQ
            _ratDriveMidEq.Peak(
                rate,
                DriveVoicing.Rat.DriveMidHz,
                DriveVoicing.Rat.DriveMidBoostDb,
                DriveVoicing.Rat.DriveMidQ);

            // 入力レベル追従の速度
            _envAttack =
                1.0 - Math.Exp(
                    -1.0 /
                    (DriveVoicing.Rat.EnvelopeAttackMs
                     * 0.001 * rate));

            _envRelease =
                1.0 - Math.Exp(
                    -1.0 /
                    (DriveVoicing.Rat.EnvelopeReleaseMs
                     * 0.001 * rate));
        }

        // ====================================
        // 共通の後段EQ
        // ====================================

        _dc.HighPass(rate, 18.0);

        _mid.Peak(
            rate,
            isSd1
                ? DriveVoicing.Sd1.MidHz
                : DriveVoicing.Rat.MidHz,
            isSd1
                ? DriveVoicing.Sd1.MidDb
                : DriveVoicing.Rat.MidDb,
            0.75);

        _body.Shelf(
            rate,
            isSd1
                ? DriveVoicing.Sd1.BodyHz
                : DriveVoicing.Rat.BodyHz,
            isSd1
                ? DriveVoicing.Sd1.BodyDb
                : DriveVoicing.Rat.BodyDb,
            false);

        // ====================================
        // TONE / FILTER
        // ====================================

        double cutoff = isSd1
            ? DriveVoicing.Sd1.ToneMinHz
              * Math.Pow(
                  DriveVoicing.Sd1.ToneMaxHz
                  / DriveVoicing.Sd1.ToneMinHz,
                  tone)
            : DriveVoicing.Rat.FilterMinHz
              * Math.Pow(
                  DriveVoicing.Rat.FilterMaxHz
                  / DriveVoicing.Rat.FilterMinHz,
                  1.0 - tone);

        _tone.LowPass(rate, cutoff);

        _settings = settings;
        _rate = rate;
    }

    // ========================================
    // 音声処理
    // ========================================

    public double Process(double input)
    {
        if (!double.IsFinite(input))
        {
            return 0.0;
        }

        if (_settings is null)
        {
            return input;
        }

        // ====================================
        // パラメーターの平滑化
        // ====================================

        _gain +=
            (_gainTarget - _gain) * _smooth;

        _level +=
            (_levelTarget - _level) * _smooth;

        _mix +=
            (_mixTarget - _mix) * _smooth;

        _driveEqMix +=
            (_driveEqTarget - _driveEqMix) * _smooth;

        _secondGain +=
            (_secondGainTarget - _secondGain) * _smooth;

        // 完全OFFなら原音を返す
        if (_mixTarget == 0.0 && _mix < 1e-8)
        {
            return input;
        }

        double clean =
            _inputLowPass.Process(
                _inputHighPass.Process(input));

        double shaped;

        // ====================================
        // SD-1
        // ====================================

        if (_kind == DriveKind.Sd1)
        {
            double driven =
                DriveCurves.Sd1(clean * _gain);

            double blend =
                DriveVoicing.Sd1.CleanBlend;

            shaped = _body.Process(
                driven * (1.0 - blend)
                + clean * blend);
        }

        // ====================================
        // RAT
        // ====================================

        else
        {
            // --------------------------------
            // 1. 歪ませる前に低域を整理し、
            //    中域を補強
            // --------------------------------

            double pre =
                _ratPreMid.Process(
                    _ratPreBass.Process(clean));

            // --------------------------------
            // 2. 弱い入力音の増幅補助
            // --------------------------------

            double magnitude = Math.Abs(pre);

            double envFactor =
                magnitude > _inputEnvelope
                    ? _envAttack
                    : _envRelease;

            _inputEnvelope +=
                (magnitude - _inputEnvelope)
                * envFactor;

            double reference = Math.Max(
                0.001,
                DriveVoicing.Rat.AutoBoostTargetRmsLike);

            double desiredBoost = Math.Clamp(
                reference /
                Math.Max(_inputEnvelope, 1e-6),
                1.0,
                DriveVoicing.Rat.MaxSingleNoteBoost);

            _inputBoost +=
                (desiredBoost - _inputBoost)
                * _smooth;

            // --------------------------------
            // 3. 第1クリッピング
            // --------------------------------

            double main =
                _ratMainClipper.Process(
                    pre * _gain * _inputBoost);

            // --------------------------------
            // 4. 第2クリッピング前のEQ
            // --------------------------------

            double secondInput =
                _ratSecondLowPass.Process(
                    _ratSecondMid.Process(
                        _ratSecondHighPass.Process(
                            main)));

            // --------------------------------
            // 5. 第2クリッピング
            // --------------------------------

            double second =
                _ratSecondClipper.Process(
                    secondInput * _secondGain
                    + DriveVoicing.Rat.SecondStageBias);

            // --------------------------------
            // 6. 2種類の歪みを合成
            // --------------------------------

            double secondMix = Math.Clamp(
                DriveVoicing.Rat.SecondStageMix,
                0.0,
                1.0);

            shaped =
                main + (second - main) * secondMix;

            // --------------------------------
            // 7. 歪み後の低域補正
            // --------------------------------

            shaped = _body.Process(shaped);
        }

        // ====================================
        // 最終EQ / FILTER
        // ====================================

        double wet =
            _tone.Process(
                _mid.Process(
                    _dc.Process(shaped)));

        // ====================================
        // RAT Drive連動EQ
        // ====================================

        if (_kind == DriveKind.Rat)
        {
            double bass =
                _ratDriveBassEq.Process(wet);

            wet +=
                (bass - wet) * _driveEqMix;

            double mid =
                _ratDriveMidEq.Process(wet);

            wet +=
                (mid - wet) * _driveEqMix;
        }

        // ====================================
        // 出力LEVEL
        // ====================================

        wet *= _level;

        // ON/OFFを滑らかに切り替える
        return input + (wet - input) * _mix;
    }

    // ========================================
    // リセット
    // ========================================

    public void Reset()
    {
        _inputHighPass.Reset();
        _inputLowPass.Reset();

        _tone.Reset();
        _mid.Reset();
        _body.Reset();
        _dc.Reset();

        _ratPreBass.Reset();
        _ratPreMid.Reset();
        _ratMainClipper.Reset();

        _ratSecondHighPass.Reset();
        _ratSecondMid.Reset();
        _ratSecondLowPass.Reset();
        _ratSecondClipper.Reset();

        _ratDriveBassEq.Reset();
        _ratDriveMidEq.Reset();

        _gain = _gainTarget = 1.0;
        _level = _levelTarget = 1.0;

        _mix = _mixTarget = 0.0;

        _driveEqMix = _driveEqTarget = 0.0;

        _secondGain = _secondGainTarget = 1.0;

        _inputEnvelope = 0.0;
        _inputBoost = 1.0;

        _settings = null;
        _rate = 0.0;
    }

    internal static double Unit(double value)
    {
        return double.IsFinite(value)
            ? Math.Clamp(value, 0.0, 1.0)
            : 0.5;
    }
}
