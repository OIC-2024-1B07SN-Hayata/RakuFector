using System;

namespace FXForge.AudioEffects;

/// <summary>
/// モノラルDelay。
/// 配列は生成時に確保し、音声スレッドだけから使用する。
/// </summary>
internal sealed class DelayStage
{
    internal const double AnalogCutoffHz = 2600;
    internal const double TapeCutoffHz = 4800;
    internal const double TapeHighPassHz = 45;

    internal const double WowHz = .27;
    internal const double WowDepthMs = .70;
    internal const double FlutterHz = 6.7;
    internal const double FlutterDepthMs = .04;

    internal const double TimeCrossfadeMs = 30;
    internal const double ParameterSmoothMs = 20;

    private readonly DelayLine[] _lines = { new(), new() };

    private int _active;
    private int _tail = -1;

    private double _rate;
    private long _revision;
    private bool _configured;

    private double _audible = 1;
    private double _audibleTarget = 1;
    private double _audibleSmooth;

    private double _lastTailWet;
    private double _cutCorrection;
    private double _cutDecay;

    public void Configure(
        DelaySettings settings,
        double rate,
        bool bypass,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!double.IsFinite(rate) ||
            rate < 8000 ||
            rate > 192000)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        if (_rate != rate)
        {
            Reset();

            _rate = rate;
            _audibleSmooth = 1 - Math.Exp(-1 / (.010 * rate));
            _cutDecay = Math.Exp(-1 / (.010 * rate));
            _audible = bypass ? 0 : 1;
        }

        if (_configured && revision != _revision)
        {
            // 直前の音色のDelayは、新しい入力を止めて残響を保持する。
            if (_tail >= 0)
                _cutCorrection += _lastTailWet;

            _tail = _active;
            _lines[_tail].StopInput();

            _active = 1 - _active;
            _lines[_active].Reset();
        }

        _lines[_active].Configure(
            settings,
            rate,
            bypass);

        _audibleTarget = bypass ? 0 : 1;
        _revision = revision;
        _configured = true;
    }

    public double Process(double input)
    {
        if (!double.IsFinite(input))
            input = 0;

        if (!_configured)
            return input;

        double wet = _lines[_active].Process(
            input,
            out double dryGain);

        _lastTailWet = _tail >= 0
            ? _lines[_tail].Process(0, out _)
            : 0;

        _audible +=
            (_audibleTarget - _audible) * _audibleSmooth;

        _cutCorrection *= _cutDecay;

        if (Math.Abs(_cutCorrection) < 1e-15)
            _cutCorrection = 0;

        double effected =
            input * dryGain +
            wet +
            _lastTailWet +
            _cutCorrection;

        return input + (effected - input) * _audible;
    }

    public void Reset()
    {
        foreach (var line in _lines)
            line.Reset();

        _active = 0;
        _tail = -1;
        _rate = 0;
        _revision = 0;
        _configured = false;

        _lastTailWet = 0;
        _cutCorrection = 0;

        _audible = 1;
        _audibleTarget = 1;
    }

    private sealed class DelayLine
    {
        // 最大192kHz・2000msと変調・補間の余裕。
        private const int Capacity = 384800;

        private readonly double[] _buffer =
            new double[Capacity];

        private int _write;
        private bool _configured;

        private double _rate;
        private double _smooth;
        private double _enableSmooth;

        private double _enabled;
        private double _enabledTarget;

        private double _mix;
        private double _mixTarget;

        private double _feedback;
        private double _feedbackTarget;

        private double _wetLevel;
        private double _wetLevelTarget;

        private double _analogWeight;
        private double _analogTarget;

        private double _tapeWeight;
        private double _tapeTarget;

        private double _requestedDelay;
        private double _fromDelay;
        private double _toDelay;

        private double _timeFade = 1;
        private double _timeStep;

        private double _analogA;
        private double _tapeA;
        private double _highPassA;

        private double _analog1;
        private double _analog2;

        private double _tapeLow;
        private double _tapePrevious;
        private double _tapeHigh;

        private double _wowPhase;
        private double _flutterPhase;
        private double _wowStep;
        private double _flutterStep;

        public void Configure(
            DelaySettings settings,
            double rate,
            bool bypass)
        {
            bool first = !_configured;

            if (first)
            {
                _rate = rate;

                _smooth = 1 - Math.Exp(
                    -1 / (ParameterSmoothMs * .001 * rate));

                _enableSmooth = 1 - Math.Exp(
                    -1 / (.010 * rate));

                _timeStep =
                    1 / (TimeCrossfadeMs * .001 * rate);

                _analogA = 1 - Math.Exp(
                    -2 * Math.PI * AnalogCutoffHz / rate);

                _tapeA = 1 - Math.Exp(
                    -2 * Math.PI * TapeCutoffHz / rate);

                _highPassA = Math.Exp(
                    -2 * Math.PI * TapeHighPassHz / rate);

                _wowStep =
                    2 * Math.PI * WowHz / rate;

                _flutterStep =
                    2 * Math.PI * FlutterHz / rate;
            }

            _enabledTarget =
                settings.Enabled && !bypass ? 1 : 0;

            // OFF後は直前の音色・音量で残響を減衰させる。
            if (settings.Enabled || first)
            {
                double time = Safe(
                    settings.EffectiveTimeMilliseconds,
                    400,
                    DelaySettings.MinimumTimeMs,
                    DelaySettings.MaximumTimeMs);

                _requestedDelay =
                    time * rate / 1000;

                _feedbackTarget = Safe(
                    settings.Feedback,
                    .35,
                    0,
                    DelaySettings.MaximumFeedback);

                _mixTarget = Safe(
                    settings.Mix,
                    .25,
                    0,
                    1);

                _wetLevelTarget = Safe(
                    settings.WetLevel,
                    1,
                    0,
                    1);

                _analogTarget =
                    settings.Mode == DelayMode.Analog ? 1 : 0;

                _tapeTarget =
                    settings.Mode == DelayMode.Tape ? 1 : 0;

                if (first)
                {
                    _fromDelay = _requestedDelay;
                    _toDelay = _requestedDelay;

                    _feedback = _feedbackTarget;
                    _mix = _mixTarget;
                    _wetLevel = _wetLevelTarget;

                    _analogWeight = _analogTarget;
                    _tapeWeight = _tapeTarget;

                    _enabled = 0;
                }
            }

            _configured = true;
        }

        public void StopInput()
        {
            _enabledTarget = 0;
        }

        public double Process(
            double input,
            out double dryGain)
        {
            if (!_configured)
            {
                dryGain = 1;
                return 0;
            }

            _enabled +=
                (_enabledTarget - _enabled) * _enableSmooth;

            _feedback +=
                (_feedbackTarget - _feedback) * _smooth;

            _mix +=
                (_mixTarget - _mix) * _smooth;

            _wetLevel +=
                (_wetLevelTarget - _wetLevel) * _smooth;

            _analogWeight +=
                (_analogTarget - _analogWeight) * _smooth;

            _tapeWeight +=
                (_tapeTarget - _tapeWeight) * _smooth;

            // 読出し位置をクロスフェードしてTime変更の段差を抑える。
            if (_timeFade >= 1 &&
                Math.Abs(_requestedDelay - _toDelay) > .001)
            {
                _fromDelay = _toDelay;
                _toDelay = _requestedDelay;
                _timeFade = 0;
            }

            double offset = _rate * .001 *
                (WowDepthMs * Math.Sin(_wowPhase) +
                 FlutterDepthMs * Math.Sin(_flutterPhase));

            double clean = Tap(0);
            double tapeRead = Tap(offset);

            _timeFade = Math.Min(
                1,
                _timeFade + _timeStep);

            _wowPhase += _wowStep;
            _flutterPhase += _flutterStep;

            if (_wowPhase >= 2 * Math.PI)
                _wowPhase -= 2 * Math.PI;

            if (_flutterPhase >= 2 * Math.PI)
                _flutterPhase -= 2 * Math.PI;

            _analog1 +=
                _analogA * (clean - _analog1);

            _analog2 +=
                _analogA * (_analog1 - _analog2);

            double analog =
                Math.Tanh(.15 * _analog2) / .15;

            _tapeLow +=
                _tapeA * (tapeRead - _tapeLow);

            _tapeHigh = _highPassA *
                (_tapeHigh + _tapeLow - _tapePrevious);

            _tapePrevious = _tapeLow;

            double tape =
                Math.Tanh(.25 * _tapeHigh) / .25;

            double colored =
                clean *
                    Math.Max(
                        0,
                        1 - _analogWeight - _tapeWeight) +
                analog * _analogWeight +
                tape * _tapeWeight;

            // LevelはFeedback経路に入れない。
            double write =
                input * _enabled +
                colored * _feedback;

            _buffer[_write] = double.IsFinite(write)
                ? Math.Clamp(write, -64, 64)
                : 0;

            if (++_write == Capacity)
                _write = 0;

            // 原音側は従来のMix処理のまま。
            dryGain = 1 - _mix * _enabled;

            // Levelは反復音の出力だけに適用する。
            double wet =
                colored * _mix * _wetLevel;

            return double.IsFinite(wet) ? wet : 0;
        }

        private double Tap(double offset)
        {
            double first =
                Read(_fromDelay + offset);

            return _timeFade >= 1
                ? Read(_toDelay + offset)
                : first +
                  (Read(_toDelay + offset) - first) * _timeFade;
        }

        private double Read(double delay)
        {
            double position =
                _write - Math.Clamp(delay, 2, Capacity - 2);

            if (position < 0)
                position += Capacity;

            int index = (int)position;

            int next = index + 1 == Capacity
                ? 0
                : index + 1;

            double fraction = position - index;

            return _buffer[index] +
                (_buffer[next] - _buffer[index]) * fraction;
        }

        public void Reset()
        {
            Array.Clear(_buffer);

            _write = 0;
            _configured = false;

            _enabled = 0;
            _enabledTarget = 0;

            _mix = 0;
            _mixTarget = 0;

            _feedback = 0;
            _feedbackTarget = 0;

            _wetLevel = 0;
            _wetLevelTarget = 0;

            _analogWeight = 0;
            _analogTarget = 0;

            _tapeWeight = 0;
            _tapeTarget = 0;

            _analog1 = 0;
            _analog2 = 0;

            _tapeLow = 0;
            _tapePrevious = 0;
            _tapeHigh = 0;

            _wowPhase = 0;
            _flutterPhase = 0;

            _timeFade = 1;
        }

        private static double Safe(
            double value,
            double fallback,
            double min,
            double max)
        {
            return double.IsFinite(value)
                ? Math.Clamp(value, min, max)
                : fallback;
        }
    }
}