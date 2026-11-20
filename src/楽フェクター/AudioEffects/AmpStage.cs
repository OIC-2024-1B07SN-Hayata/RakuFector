using System;

namespace FXForge.AudioEffects;

/// <summary>
/// 既存のクリーンAMPに独立したHIGH調整を追加。
/// HIGH中央時は従来の信号経路を維持する。
/// 音声スレッドからのみ使用する。
/// </summary>
internal sealed class AmpStage
{
    public bool BrightEnabled { get; set; } = false;
    public bool InternalSpeakerEnabled { get; set; } = true;

    private const double InputHighPassHz = 35.0;

    private const double BassHz = 130.0;
    private const double MiddleHz = 780.0;
    private const double TrebleHz = 3200.0;

    private const double BassRangeDb = 10.0;
    private const double MiddleRangeDb = 9.0;
    private const double TrebleRangeDb = 10.0;

    // HIGHはTrebleとは別の、より高い帯域を調整。
    private const double HighHz = 6000.0;
    private const double HighRangeDb = 6.0;
    private const double HighQ = .80;

    private const double BrightHz = 2400.0;
    private const double BrightBoostDb = 2.8;

    private const double MaxPreampGain = 3.8;

    // 現在の実コードの値を維持。
    private const double MaxOutputLevel = 30.0;

    private const double SmoothTimeSeconds = .008;

    private const double SpeakerHighPassHz = 70.0;
    private const double SpeakerBassHz = 130.0;
    private const double SpeakerLowMidHz = 430.0;
    private const double SpeakerPresenceHz = 2700.0;

    private const double SpeakerLowPassHz1 = 7000.0;
    private const double SpeakerLowPassHz2 = 8500.0;

    private const double CleanHeadroomKnee = 1.15;
    private const double CleanHeadroomRange = .65;

    private readonly Biquad _inputHighPass = new();

    private readonly Biquad _bass = new();
    private readonly Biquad _middle = new();
    private readonly Biquad _treble = new();

    private readonly Biquad _highBoost = new();
    private readonly Biquad _highCut = new();

    private readonly Biquad _bright = new();

    private readonly Biquad _speakerHighPass = new();
    private readonly Biquad _speakerBass = new();
    private readonly Biquad _speakerLowMid = new();
    private readonly Biquad _speakerPresence = new();
    private readonly Biquad _speakerLowPass1 = new();
    private readonly Biquad _speakerLowPass2 = new();

    private double _rate;
    private double _smooth;

    private double _gain = 1.0;
    private double _gainTarget = 1.0;

    private double _level = 1.0;
    private double _levelTarget = 1.0;

    private double _mix;
    private double _mixTarget;

    private double _highAmount;
    private double _highTarget;

    private double _brightMix;
    private double _brightTarget;

    private double _speakerMix;
    private double _speakerTarget;

    private bool _configured;

    private bool _lastEnabled;
    private bool _lastBright;
    private bool _lastSpeaker;

    private double _lastGain;
    private double _lastBass;
    private double _lastMiddle;
    private double _lastTreble;
    private double _lastHigh;
    private double _lastLevel;

    public void Reset()
    {
        _inputHighPass.Reset();

        _bass.Reset();
        _middle.Reset();
        _treble.Reset();

        _highBoost.Reset();
        _highCut.Reset();

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
        _highAmount = _highTarget = 0.0;
        _brightMix = _brightTarget = 0.0;
        _speakerMix = _speakerTarget = 0.0;

        _configured = false;
    }

    public void Configure(AmpSettings settings, double rate)
    {
        ArgumentNullException.ThrowIfNull(settings);

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
        double high = DriveStage.Unit(settings.High);
        double level = DriveStage.Unit(settings.Level);

        bool enabled = settings.Enabled;
        bool bright = BrightEnabled;
        bool speaker = InternalSpeakerEnabled;

        if (_configured &&
            _rate == rate &&
            _lastEnabled == enabled &&
            _lastBright == bright &&
            _lastSpeaker == speaker &&
            _lastGain == gain &&
            _lastBass == bass &&
            _lastMiddle == middle &&
            _lastTreble == treble &&
            _lastHigh == high &&
            _lastLevel == level)
        {
            return;
        }

        bool newRate = !_configured || _rate != rate;

        if (newRate)
            Reset();

        _rate = rate;

        _smooth = 1.0 - Math.Exp(
            -1.0 / (SmoothTimeSeconds * rate));

        _gainTarget =
            1.0 +
            (MaxPreampGain - 1.0) * gain * gain;

        _levelTarget = MaxOutputLevel * level;

        _mixTarget = enabled ? 1.0 : 0.0;
        _brightTarget = bright ? 1.0 : 0.0;
        _speakerTarget = speaker ? 1.0 : 0.0;

        // High 0＝最大カット、50＝補正なし、100＝最大ブースト。
        _highTarget = (high - .5) * 2.0;

        if (newRate)
        {
            _gain = _gainTarget;
            _level = _levelTarget;
            _mix = _mixTarget;
            _highAmount = _highTarget;
            _brightMix = _brightTarget;
            _speakerMix = _speakerTarget;
        }

        _inputHighPass.HighPass(rate, InputHighPassHz);

        _bass.Shelf(
            rate,
            BassHz,
            (bass - .5) * 2.0 * BassRangeDb,
            false);

        _middle.Peak(
            rate,
            MiddleHz,
            (middle - .5) * 2.0 * MiddleRangeDb,
            .75);

        _treble.Shelf(
            rate,
            TrebleHz,
            (treble - .5) * 2.0 * TrebleRangeDb,
            true);

        // 係数は固定し、HIGHノブによる混合率を平滑化する。
        double highFrequency = Math.Min(HighHz, rate * .45);

        _highBoost.Peak(
            rate,
            highFrequency,
            HighRangeDb,
            HighQ);

        _highCut.Peak(
            rate,
            highFrequency,
            -HighRangeDb,
            HighQ);

        _bright.Shelf(
            rate,
            BrightHz,
            BrightBoostDb,
            true);

        _speakerHighPass.HighPass(rate, SpeakerHighPassHz);

        _speakerBass.Peak(
            rate,
            SpeakerBassHz,
            1.2,
            .75);

        _speakerLowMid.Peak(
            rate,
            SpeakerLowMidHz,
            -1.3,
            .9);

        _speakerPresence.Peak(
            rate,
            SpeakerPresenceHz,
            1.6,
            1.1);

        _speakerLowPass1.LowPass(rate, SpeakerLowPassHz1);
        _speakerLowPass2.LowPass(rate, SpeakerLowPassHz2);

        _lastEnabled = enabled;
        _lastBright = bright;
        _lastSpeaker = speaker;

        _lastGain = gain;
        _lastBass = bass;
        _lastMiddle = middle;
        _lastTreble = treble;
        _lastHigh = high;
        _lastLevel = level;

        _configured = true;
    }

    public double Process(double input)
    {
        if (!_configured)
            return double.IsFinite(input) ? input : 0.0;

        if (!double.IsFinite(input))
            input = 0.0;

        _gain += (_gainTarget - _gain) * _smooth;
        _level += (_levelTarget - _level) * _smooth;
        _mix += (_mixTarget - _mix) * _smooth;

        _highAmount +=
            (_highTarget - _highAmount) * _smooth;

        _brightMix +=
            (_brightTarget - _brightMix) * _smooth;

        _speakerMix +=
            (_speakerTarget - _speakerMix) * _smooth;

        if (_mixTarget == 0.0 && _mix < 1e-8)
            return input;

        double x = _inputHighPass.Process(input);

        x *= _gain;

        x = _bass.Process(x);
        x = _middle.Process(x);
        x = _treble.Process(x);

        double boostedHigh = _highBoost.Process(x);
        double cutHigh = _highCut.Process(x);

        if (_highAmount > 0)
        {
            x += (boostedHigh - x) * _highAmount;
        }
        else if (_highAmount < 0)
        {
            x += (cutHigh - x) * -_highAmount;
        }

        double bright = _bright.Process(x);
        x += (bright - x) * _brightMix;

        x = LimitExceptionalPeaks(x);

        double speaker = _speakerHighPass.Process(x);

        speaker = _speakerBass.Process(speaker);
        speaker = _speakerLowMid.Process(speaker);
        speaker = _speakerPresence.Process(speaker);
        speaker = _speakerLowPass1.Process(speaker);
        speaker = _speakerLowPass2.Process(speaker);

        x += (speaker - x) * _speakerMix;

        double wet = x * _level;

        return input + (wet - input) * _mix;
    }

    private static double LimitExceptionalPeaks(double x)
    {
        double magnitude = Math.Abs(x);

        if (magnitude <= CleanHeadroomKnee)
            return x;

        double shaped =
            CleanHeadroomKnee +
            CleanHeadroomRange *
            (1.0 - Math.Exp(
                -(magnitude - CleanHeadroomKnee) /
                CleanHeadroomRange));

        return Math.CopySign(shaped, x);
    }
}