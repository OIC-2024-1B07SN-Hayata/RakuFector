
using System;

namespace FXForge.AudioEffects;

/// <summary>
/// JC-120 inspired CLEAN amp.
/// JC-120の特徴を参考にした近似モデル。
/// 実機回路を完全に再現したものではありません。
/// </summary>
internal sealed class AmpStage
{
    // ========================================
    // 基本設定
    // ========================================

    public bool BrightEnabled { get; set; } = false;

    // 別のキャビネットIRを使用する場合はfalse
    public bool InternalSpeakerEnabled { get; set; } = true;

    // ========================================
    // 音作りパラメータ
    // ========================================

    private const double InputHighPassHz = 35.0;

    private const double BassHz = 130.0;
    private const double MiddleHz = 780.0;
    private const double TrebleHz = 3200.0;

    private const double BassRangeDb = 10.0;
    private const double MiddleRangeDb = 9.0;
    private const double TrebleRangeDb = 10.0;

    private const double BrightHz = 2400.0;
    private const double BrightBoostDb = 2.8;

    // プリアンプ最大増幅倍率
    private const double MaxPreampGain = 3.8;

    // ★ 音量改善
    // 以前の2.0から2.5に変更
    private const double MaxOutputLevel = 30.0;

    // パラメータ変更時の平滑化時間
    private const double SmoothTimeSeconds = 0.008;

    // ========================================
    // スピーカー特性（簡易近似）
    // ========================================

    private const double SpeakerHighPassHz = 70.0;
    private const double SpeakerBassHz = 130.0;
    private const double SpeakerLowMidHz = 430.0;
    private const double SpeakerPresenceHz = 2700.0;

    private const double SpeakerLowPassHz1 = 7000.0;
    private const double SpeakerLowPassHz2 = 8500.0;

    // ========================================
    // クリーンヘッドルーム
    // ========================================

    private const double CleanHeadroomKnee = 1.15;
    private const double CleanHeadroomRange = 0.65;

    // ========================================
    // フィルター
    // ========================================

    private readonly Biquad _inputHighPass = new();

    private readonly Biquad _bass = new();
    private readonly Biquad _middle = new();
    private readonly Biquad _treble = new();
    private readonly Biquad _bright = new();

    private readonly Biquad _speakerHighPass = new();
    private readonly Biquad _speakerBass = new();
    private readonly Biquad _speakerLowMid = new();
    private readonly Biquad _speakerPresence = new();
    private readonly Biquad _speakerLowPass1 = new();
    private readonly Biquad _speakerLowPass2 = new();

    // ========================================
    // 内部状態
    // ========================================

    private double _rate;

    private double _gain = 1.0;
    private double _gainTarget = 1.0;

    private double _level = 1.0;
    private double _levelTarget = 1.0;

    private double _mix;
    private double _mixTarget;

    private double _brightMix;
    private double _brightTarget;

    private double _speakerMix;
    private double _speakerTarget;

    private double _smooth;

    private bool _configured;

    private bool _lastEnabled;
    private bool _lastBright;
    private bool _lastSpeaker;

    private double _lastGain;
    private double _lastBass;
    private double _lastMiddle;
    private double _lastTreble;
    private double _lastLevel;

    // ========================================
    // 状態リセット
    // ========================================

    public void Reset()
    {
        _inputHighPass.Reset();

        _bass.Reset();
        _middle.Reset();
        _treble.Reset();
        _bright.Reset();

        _speakerHighPass.Reset();
        _speakerBass.Reset();
        _speakerLowMid.Reset();
        _speakerPresence.Reset();
        _speakerLowPass1.Reset();
        _speakerLowPass2.Reset();

        _gain = _gainTarget = 1.0;
        _level = _levelTarget = 1.0;

        _mix = _mixTarget = 0.0;
        _brightMix = _brightTarget = 0.0;
        _speakerMix = _speakerTarget = 0.0;

        _configured = false;
    }

    // ========================================
    // アンプ設定
    // ========================================

    public void Configure(AmpSettings settings, double rate)
    {
        if (!double.IsFinite(rate) ||
            rate < 8000.0 ||
            rate > 1536000.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        double gain = DriveStage.Unit(settings.Gain);
        double bass = DriveStage.Unit(settings.Bass);
        double middle = DriveStage.Unit(settings.Middle);
        double treble = DriveStage.Unit(settings.Treble);
        double level = DriveStage.Unit(settings.Level);

        bool enabled = settings.Enabled;
        bool bright = BrightEnabled;
        bool speaker = InternalSpeakerEnabled;

        // 設定に変更がなければ再計算しない
        if (_configured &&
            _rate == rate &&
            _lastEnabled == enabled &&
            _lastBright == bright &&
            _lastSpeaker == speaker &&
            _lastGain == gain &&
            _lastBass == bass &&
            _lastMiddle == middle &&
            _lastTreble == treble &&
            _lastLevel == level)
        {
            return;
        }

        bool newRate = !_configured || _rate != rate;

        if (newRate)
        {
            Reset();
        }

        _rate = rate;

        _smooth = 1.0 - Math.Exp(
            -1.0 / (SmoothTimeSeconds * rate));

        // ====================================
        // GAIN
        // ====================================

        _gainTarget =
            1.0 +
            (MaxPreampGain - 1.0) * gain * gain;

        // ====================================
        // LEVEL ★ 音量改善箇所
        // ====================================

        // 旧仕様：
        // _levelTarget = 2.0 * level * level;

        // 新仕様：
        // Level 50 = 1.25倍
        // Level 80 = 2.00倍
        // Level 100 = 2.50倍

        _levelTarget = MaxOutputLevel * level;

        _mixTarget = enabled ? 1.0 : 0.0;
        _brightTarget = bright ? 1.0 : 0.0;
        _speakerTarget = speaker ? 1.0 : 0.0;

        if (newRate)
        {
            _gain = _gainTarget;
            _level = _levelTarget;
            _mix = _mixTarget;
            _brightMix = _brightTarget;
            _speakerMix = _speakerTarget;
        }

        // ====================================
        // JC-120風 EQ
        // ====================================

        _inputHighPass.HighPass(
            rate,
            InputHighPassHz);

        _bass.Shelf(
            rate,
            BassHz,
            (bass - 0.5) * 2.0 * BassRangeDb,
            false);

        _middle.Peak(
            rate,
            MiddleHz,
            (middle - 0.5) * 2.0 * MiddleRangeDb,
            0.75);

        _treble.Shelf(
            rate,
            TrebleHz,
            (treble - 0.5) * 2.0 * TrebleRangeDb,
            true);

        _bright.Shelf(
            rate,
            BrightHz,
            BrightBoostDb,
            true);

        // ====================================
        // スピーカー音響補正
        // ====================================

        _speakerHighPass.HighPass(
            rate,
            SpeakerHighPassHz);

        _speakerBass.Peak(
            rate,
            SpeakerBassHz,
            1.2,
            0.75);

        _speakerLowMid.Peak(
            rate,
            SpeakerLowMidHz,
            -1.3,
            0.9);

        _speakerPresence.Peak(
            rate,
            SpeakerPresenceHz,
            1.6,
            1.1);

        _speakerLowPass1.LowPass(
            rate,
            SpeakerLowPassHz1);

        _speakerLowPass2.LowPass(
            rate,
            SpeakerLowPassHz2);

        // ====================================
        // 設定キャッシュ
        // ====================================

        _lastEnabled = enabled;
        _lastBright = bright;
        _lastSpeaker = speaker;

        _lastGain = gain;
        _lastBass = bass;
        _lastMiddle = middle;
        _lastTreble = treble;
        _lastLevel = level;

        _configured = true;
    }

    // ========================================
    // 音声処理
    // ========================================

    public double Process(double input)
    {
        if (!_configured)
        {
            return double.IsFinite(input) ? input : 0.0;
        }

        if (!double.IsFinite(input))
        {
            input = 0.0;
        }

        // ====================================
        // パラメータの平滑化
        // ====================================

        _gain += (_gainTarget - _gain) * _smooth;
        _level += (_levelTarget - _level) * _smooth;
        _mix += (_mixTarget - _mix) * _smooth;

        _brightMix +=
            (_brightTarget - _brightMix) * _smooth;

        _speakerMix +=
            (_speakerTarget - _speakerMix) * _smooth;

        // アンプOFF
        if (_mixTarget == 0.0 && _mix < 1e-8)
        {
            return input;
        }

        // ====================================
        // プリアンプ処理
        // ====================================

        double x = _inputHighPass.Process(input);

        // GAINを適用
        x *= _gain;

        // ====================================
        // 3バンド EQ
        // ====================================

        x = _bass.Process(x);
        x = _middle.Process(x);
        x = _treble.Process(x);

        // ====================================
        // BRIGHT
        // ====================================

        double bright = _bright.Process(x);

        x += (bright - x) * _brightMix;

        // ====================================
        // 過大ピークのソフト制限
        // ====================================

        x = LimitExceptionalPeaks(x);

        // ====================================
        // スピーカー音響補正
        // ====================================

        double speaker = _speakerHighPass.Process(x);

        speaker = _speakerBass.Process(speaker);
        speaker = _speakerLowMid.Process(speaker);
        speaker = _speakerPresence.Process(speaker);
        speaker = _speakerLowPass1.Process(speaker);
        speaker = _speakerLowPass2.Process(speaker);

        x += (speaker - x) * _speakerMix;

        // ====================================
        // 最終音量 ★
        // ====================================

        double wet = x * _level;

        // ON/OFF時に急激に切り替えない
        return input + (wet - input) * _mix;
    }

    // ========================================
    // 過大ピークのソフト制限
    // ========================================

    private static double LimitExceptionalPeaks(double x)
    {
        double magnitude = Math.Abs(x);

        if (magnitude <= CleanHeadroomKnee)
        {
            return x;
        }

        double shaped =
            CleanHeadroomKnee +
            CleanHeadroomRange *
            (1.0 - Math.Exp(
                -(magnitude - CleanHeadroomKnee) /
                CleanHeadroomRange));

        return Math.CopySign(shaped, x);
    }
}
