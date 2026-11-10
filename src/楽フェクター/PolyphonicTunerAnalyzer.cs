using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace FXForge.Desktop.Views; // 6弦解析用クラス

/// <summary>開放弦1本分の解析結果。未検出の周波数とcentはnull。</summary>
public sealed class StringTuningResult
{
    public int StringNumber { get; init; }
    public string TargetNote { get; init; } = "";
    public double TargetFrequency { get; init; }
    public double? Frequency { get; init; }
    public double? Cents { get; init; }
    // 0～1 の相対的な信号強度。厳密な検出確率ではありません。
    public double Confidence { get; init; }
    public bool IsDetected => Frequency.HasValue;
}

/// <summary>
/// 標準チューニング(E2 A2 D3 G3 B3 E4)の開放弦専用、6弦同時解析。
/// FFTで得た振幅スペクトルに6つの倍音テンプレートを同時フィットする。
/// 短時間の音声コピーだけをASIOスレッドで行い、FFTはワーカースレッドで実行する。
/// ※試作アルゴリズム。ギター実音で閾値やモデルの調整が必要です。
/// </summary>
public sealed class PolyphonicTunerAnalyzer
{
    private const int WindowSize = 32768;
    private const int FftSize = 65536; // ゼロ詰め補間用。真の周波数分解能が2倍になるわけではない。
    private const int HopSize = 8192;
    private const int HarmonicCount = 8;
    private const double SearchCents = 75.0;
    private const double CentStep = 2.0;

    private static readonly string[] Notes = { "E2", "A2", "D3", "G3", "B3", "E4" };
    private static readonly double[] Targets =
    {
        82.4068892282, 110.0, 146.8323839587,
        195.9977179909, 246.9416506281, 329.6275569129
    };

    private readonly object _sync = new();
    private readonly float[] _ring = new float[WindowSize];
    private int _writePosition;
    private int _available;
    private int _samplesSinceLast;
    private int _sampleRate;
    private int _busy;

    /// <summary>解析完了時にワーカースレッドから通知されます。</summary>
    public event Action<StringTuningResult[]>? ResultsReady;

    /// <summary>
    /// AudioReceivedから呼ぶ。引数のsamplesは使い回されるため、その場でリングバッファにコピーする。
    /// CommonAudioInputが渡す、選択済み入力1チャンネルのモノラルfloat PCMを想定。
    /// </summary>
    public void AddSamples(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0 || sampleRate <= 0) return;

        float[]? frame = null;
        lock (_sync)
        {
            if (_sampleRate != sampleRate)
            {
                _sampleRate = sampleRate;
                _writePosition = 0;
                _available = 0;
                _samplesSinceLast = 0;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                _ring[_writePosition] = samples[i];
                _writePosition = (_writePosition + 1) % WindowSize;
                if (_available < WindowSize) _available++;
            }
            _samplesSinceLast += samples.Length;

            if (_available == WindowSize && _samplesSinceLast >= HopSize)
            {
                _samplesSinceLast = 0;
                if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
                {
                    frame = new float[WindowSize];
                    // _writePositionは最古のサンプルの位置。
                    int tail = WindowSize - _writePosition;
                    Array.Copy(_ring, _writePosition, frame, 0, tail);
                    Array.Copy(_ring, 0, frame, tail, _writePosition);
                }
            }
        }

        if (frame == null) return;
        float[] capturedFrame = frame;
        int capturedRate = sampleRate;
        _ = Task.Run(() =>
        {
            try
            {
                StringTuningResult[] result = Analyze(capturedFrame, capturedRate);
                ResultsReady?.Invoke(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("6弦解析エラー: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private static StringTuningResult[] Analyze(float[] samples, int sampleRate)
    {
        double sum = 0;
        double average = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            average += samples[i];
            sum += (double)samples[i] * samples[i];
        }
        average /= samples.Length;
        double rms = Math.Sqrt(sum / samples.Length);
        if (rms < 0.002) return CreateUndetected();

        Complex[] fft = new Complex[FftSize];
        for (int i = 0; i < WindowSize; i++)
        {
            double hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (WindowSize - 1));
            fft[i] = new Complex((samples[i] - average) * hann, 0);
        }
        FFT(fft);

        // 倍音最大値はE4の8倍音付近。少し余裕を持たせる。
        int maxBin = Math.Min(FftSize / 2 - 1,
            (int)Math.Ceiling(3200.0 * FftSize / sampleRate));
        double[] spectrum = new double[maxBin + 1];
        for (int i = 0; i < spectrum.Length; i++)
            spectrum[i] = fft[i].Magnitude;

        // 基準音±75centを2cent刻みで探索。
        // 6弦の候補を「同じ残差スペクトル」に当てはめるので倍音の二重カウントを減らせる。
        int candidateCount = (int)Math.Round(SearchCents * 2 / CentStep) + 1;
        Template[][] bank = new Template[6][];
        for (int s = 0; s < 6; s++)
        {
            bank[s] = new Template[candidateCount];
            for (int j = 0; j < candidateCount; j++)
            {
                double cents = -SearchCents + j * CentStep;
                double f = Targets[s] * Math.Pow(2, cents / 1200.0);
                bank[s][j] = BuildTemplate(f, cents, sampleRate, maxBin);
            }
        }

        // 非負の振幅を持つ6つの倍音テンプレートを座標降下で同時推定。
        // 各弦を更新する際は前回の自分の成分を残差に戻す。
        double[] residual = (double[])spectrum.Clone();
        Template?[] selected = new Template?[6];
        double[] amplitudes = new double[6];
        for (int pass = 0; pass < 5; pass++)
        {
            for (int order = 0; order < 6; order++)
            {
                int s = pass % 2 == 0 ? order : 5 - order;
                if (selected[s] != null)
                    AddToResidual(residual, selected[s]!, amplitudes[s]);

                double bestGain = -1;
                double bestAmplitude = 0;
                Template? bestTemplate = null;
                foreach (Template t in bank[s])
                {
                    double dot = DotResidual(residual, t);
                    double positiveDot = Math.Max(0, dot);
                    double gain = positiveDot * positiveDot / t.NormSquared;
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestAmplitude = positiveDot / t.NormSquared;
                        bestTemplate = t;
                    }
                }
                selected[s] = bestTemplate;
                amplitudes[s] = bestAmplitude;
                if (bestTemplate != null)
                    AddToResidual(residual, bestTemplate, -bestAmplitude);
            }
        }

        double strongest = 0;
        foreach (double amp in amplitudes) strongest = Math.Max(strongest, amp);
        if (strongest <= 0) return CreateUndetected();

        StringTuningResult[] results = new StringTuningResult[6];
        for (int s = 0; s < 6; s++)
        {
            double strength = Math.Clamp(amplitudes[s] / strongest, 0, 1);
            // 単なるノイズや他弦の倍音だけから生じた弱い候補は表示しない。
            //bool detected = selected[s] != null && strength >= 0.08;

            // s == 5 が第1弦（E4）
            // 第1弦だけ、他の弦より弱い音でも表示する
            double threshold = s == 5 ? 0.06 : 0.08;
            bool detected = selected[s] != null && strength >= threshold;

            results[s] = new StringTuningResult
            {
                StringNumber = 6 - s,
                TargetNote = Notes[s],
                TargetFrequency = Targets[s],
                Frequency = detected ? selected[s]!.Frequency : null,
                Cents = detected ? selected[s]!.Cents : null,
                Confidence = strength
            };
        }
        return results;
    }

    private static StringTuningResult[] CreateUndetected()
    {
        StringTuningResult[] result = new StringTuningResult[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = new StringTuningResult
            {
                StringNumber = 6 - i,
                TargetNote = Notes[i],
                TargetFrequency = Targets[i]
            };
        }
        return result;
    }

    private sealed class Template
    {
        public double Frequency { get; init; }
        public double Cents { get; init; }
        public int[] Bins { get; init; } = Array.Empty<int>();
        public double[] Weights { get; init; } = Array.Empty<double>();
        public double NormSquared { get; init; }
    }

    private static Template BuildTemplate(double frequency, double cents, int sampleRate, int maxBin)
    {
        // 65536点FFT上のHann窓由来の広がりをガウス関数で近似する。
        // 倍音はh^-0.92で減衰する簡易モデル。実楽器では変化する。
        const double sigma = 1.65;
        int[] bins = new int[HarmonicCount * 13];
        double[] weights = new double[HarmonicCount * 13];
        int count = 0;
        double norm = 0;
        for (int h = 1; h <= HarmonicCount; h++)
        {
            double center = frequency * h * FftSize / sampleRate;
            double harmonicWeight = 1.0 / Math.Pow(h, 0.92);
            int first = (int)Math.Floor(center) - 6;
            for (int bin = first; bin <= first + 12; bin++)
            {
                if (bin < 1 || bin > maxBin) continue;
                double delta = (bin - center) / sigma;
                double weight = harmonicWeight * Math.Exp(-0.5 * delta * delta);
                bins[count] = bin;
                weights[count] = weight;
                norm += weight * weight;
                count++;
            }
        }
        Array.Resize(ref bins, count);
        Array.Resize(ref weights, count);
        return new Template
        {
            Frequency = frequency,
            Cents = cents,
            Bins = bins,
            Weights = weights,
            NormSquared = Math.Max(norm, 1e-12)
        };
    }

    private static double DotResidual(double[] residual, Template template)
    {
        double dot = 0;
        for (int i = 0; i < template.Bins.Length; i++)
            dot += residual[template.Bins[i]] * template.Weights[i];
        return dot;
    }

    private static void AddToResidual(double[] residual, Template template, double amount)
    {
        for (int i = 0; i < template.Bins.Length; i++)
            residual[template.Bins[i]] += amount * template.Weights[i];
    }

    private static void FFT(Complex[] buffer)
    {
        int n = buffer.Length;
        int j = 0;
        for (int i = 1; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j) (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            Complex rotation = new(Math.Cos(angle), Math.Sin(angle));
            for (int offset = 0; offset < n; offset += length)
            {
                Complex w = Complex.One;
                for (int k = 0; k < length / 2; k++)
                {
                    Complex even = buffer[offset + k];
                    Complex odd = buffer[offset + k + length / 2] * w;
                    buffer[offset + k] = even + odd;
                    buffer[offset + k + length / 2] = even - odd;
                    w *= rotation;
                }
            }
        }
    }
}
