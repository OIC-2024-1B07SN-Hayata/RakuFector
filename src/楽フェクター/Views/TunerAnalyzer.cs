using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Numerics;  // Complex型用　FFTの周波数計算に使用


namespace FXForge.Desktop.Views
{
    public class TunerResult  // チューナーの解析結果をまとめて保存
    {
        /// 周波数
        public double frequency { get; set; }


        /// 音名
        /// 例：E2
        public string note { get; set; } = "";


        /// 基準音からのずれ
        /// -50 ～ +50 cent
        public double cents { get; set; }


        /// <summary>
        /// 入力チャンネル
        /// </summary>
        public int InputChannel { get; set; }


        /// サンプリング周波数
        public int SampleRate { get; set; }

        /// 音声信号
        public bool hasSignal = true;
        // 現在のコードでは音声が小さすぎると解析結果そのものが null になるため、音声がないときに hasSignal = false を返す仕組みにはなっていない。
    }



    public class TunerAnalyzer// float配列から音程を解析するチューナークラス
    {


        bool hassignal = true; //現状は値を書き換える処理がないので、常に true のままになっている。

        // ============================================================
        // FFT設定
        // ============================================================

        // FFTするサンプル数
        private const int FftSize = 32768;  // サンプリング周波数が48kHzの場合、約0.683秒分の音声


        // 次の解析までに進めるサンプル数
        private const int HopSize = 4096;


        // 入力データをためておく配列
        private readonly float[] _sampleBuffer =　new float[FftSize];
        // readonly = フィールドに別の配列を再代入できない(配列内の各要素の値は変更できる)


        // 現在何サンプル入っているか
        private int _bufferCount = 0;


        // 前回のサンプリング周波数
        private int _lastSampleRate = 0;


        // 基準ピッチ
        // A4 = 440Hz
        private double _referencePitch = 440.0;


        // 周波数を滑らかにするため
        private double? _smoothedFrequency = null;



        // ============================================================
        // 基準ピッチ
        // ============================================================

        public double ReferencePitch //外部のクラスから基準ピッチを取得・変更できるようにするためのプロパティ
        {
            get
            {
                return _referencePitch;　// 現在保存されている基準ピッチ
            }

            set
            {
                if (value > 0)
                {
                    _referencePitch = value;
                }
            }
        }



        // ============================================================
        // 入力共通クラスから受け取ったデータをチューナーへ入力
        // ============================================================

        public TunerResult? AddSamples(　// 音声データを受け取り、解析結果を返すメソッドを定義する(TunerPageで使用)
            float[] samples,
            int sampleRate,
            int inputChannel)
        {

            // データがあるか確認
            if (samples == null || samples.Length == 0)
            {
                return null;
            }


            // サンプリング周波数がおかしいか確認
            if (sampleRate <= 0)
            {
                return null;
            }


            // ========================================================
            // サンプリング周波数が途中で変わった場合
            // バッファをリセットする
            // ========================================================

            if (_lastSampleRate != 0 && _lastSampleRate != sampleRate)
            {
                Reset(); // Resetメソッドの呼び出し
            }



            _lastSampleRate = sampleRate;



            TunerResult? latestResult = null; //addSampleで得られた最新の解析結果を保存する変数



            // ========================================================
            // float配列
            //
            // ↓
            //
            // 32768サンプルたまるまで保存
            // ========================================================

            for (int i = 0;i < samples.Length;i++)
            {
                _sampleBuffer[_bufferCount] =  samples[i];


                _bufferCount++;


                // ====================================================
                // 32768サンプルたまった
                // ====================================================

                if (_bufferCount >= FftSize)
                {
                    // 実際の解析に使用する音声データを保存する配列を宣言後、配列を生成(FFT解析専用のデータ)
                    float[] analysisSamples = new float[FftSize]; 

                    
                    // Array.Copy()メソッドを呼び出す(コピー元配列,コピー先配列,コピーするデータ数)
                    Array.Copy(_sampleBuffer,analysisSamples,FftSize);


                    // =================================================
                    // 周波数解析
                    // =================================================

                    latestResult =　// これから呼び出すメソッドの戻り値をこの変数に保存する
                        AnalyzeFrequency(　// FFT・HPSなどを使用して周波数を解析するメソッドの呼び出し
                            analysisSamples,　// 解析対象の音声データを渡す
                            sampleRate,　// サンプリング周波数を渡す
                            inputChannel);  // 入力チャンネル番号を渡す







                    // =================================================
                    // オーバーラップ
                    //
                    // 全データを捨てずに、
                    // 一部を次の解析にも使用する。
                    // =================================================

                    Array.Copy(　// 配列のコピー処理を開始
                        _sampleBuffer,  // コピー元の配列
                        HopSize, // コピーを開始する位置を4096番目に指定する(先頭の4096個を除いた位置からコピーする)
                        _sampleBuffer,  // コピー先も同じ配列を指定する
                        0,  // コピー先の開始位置を0番目に指定する
                        FftSize - HopSize);　// コピーするデータ数を指定する


                    // 現在保存しているサンプル数を更新(残した28672個を設定し次に4096個追加されると周波数解析が実行されるようにする)
                    _bufferCount = FftSize - HopSize;
                }
            }


            return latestResult;  // 解析結果をAddSamples()の呼び出し元に返す
        }



        // ============================================================
        // 周波数解析
        // ============================================================

        // 周波数解析を行うメソッドを定義する(解析失敗時はnullを返す)
        private TunerResult? AnalyzeFrequency(float[] samples,int sampleRate,int inputChannel)
        {
            // ========================================================
            // 音量確認
            // ========================================================

            double rms = CalculateRms(samples);


            // 音が小さすぎる場合
            if (rms < 0.002)
            {
                _smoothedFrequency = null;

                return null;
            }


            // ========================================================
            // 波形の平均値を求める
            //
            // DC成分除去用
            // ========================================================

            double average = 0;


            for (int i = 0;i < samples.Length;i++)
            {
                average += samples[i];
            }


            average /= samples.Length;


            // ========================================================
            // FFT用Complex配列
            // ========================================================

            Complex[] fftData = new Complex[FftSize];


            // ========================================================
            // Hann窓  この計算で配列の先頭と末尾は係数が0になり、中央付近では約1になる
            // ========================================================

            for (int i = 0;i < FftSize;i++)
            {
                double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1));


                double sample = (samples[i] - average) * hann;


                fftData[i] = new Complex(sample,0);
            }


            // ========================================================
            // FFT
            // ========================================================

            FFT(fftData);　// メソッドを呼び出して周波数を解析する


            // ========================================================
            // 振幅スペクトル　＝　それぞれの周波数成分の大きさを表すもの
            // ========================================================

            int half = FftSize / 2;


            double[] magnitude = new double[half];


            for (int i = 0;i < half;i++)
            {
                magnitude[i] = fftData[i].Magnitude;
            }


            // ========================================================
            // ギターで使用する周波数範囲
            //
            // 約70Hz ～ 1400Hz
            // ========================================================

            int minBin = (int)Math.Ceiling(70.0 * FftSize / sampleRate);


            int maxBin = (int)Math.Floor(1400.0 * FftSize / sampleRate);


            // 3倍音まで使用するので
            // 配列範囲を超えないようにする
            maxBin = Math.Min(maxBin,half / 3 - 2);


            if (minBin < 2)
            {
                minBin = 2;
            }


            if (maxBin <= minBin)
            {
                return null;
            }


            // ========================================================
            // 最大振幅を探す
            // ========================================================

            double maxMagnitude = 0;


            int magnitudeSearchMax = Math.Min(half - 1,maxBin * 3);


            for (int i = minBin;i <= magnitudeSearchMax;i++)
            {
                if (magnitude[i] > maxMagnitude)
                {
                    maxMagnitude = magnitude[i];
                }
            }


            if (maxMagnitude <= 0)
            {
                return null;
            }


            // ========================================================
            // HPS
            //
            // Harmonic Product Spectrum
            //
            // 基本波
            // 2倍音
            // 3倍音
            //
            // を使用して基本周波数を探す
            // ========================================================

            double[] scores = new double[maxBin + 2];


            for (int i = 0;i < scores.Length;i++)
            {
                scores[i] = double.NegativeInfinity;
            }


            int bestBin = -1;


            double bestScore =  double.NegativeInfinity;


            const double epsilon = 0.000000000001;


            for (int bin = minBin;bin <= maxBin;bin++)
            {
                // 基本波が小さすぎる候補は除外
                if (magnitude[bin] < maxMagnitude * 0.005)
                {
                    continue;
                }


                double score = Math.Log(magnitude[bin] + epsilon);


                score += 0.60 * Math.Log(magnitude[bin * 2] + epsilon);


                score += 0.35 * Math.Log(magnitude[bin * 3] + epsilon);


                scores[bin] = score;


                if (score > bestScore)
                {
                    bestScore = score;

                    bestBin = bin;
                }
            }


            if (bestBin <= minBin || bestBin >= maxBin)
            {
                return null;
            }


            // ========================================================
            // 放物線補間
            //
            // FFTの1目盛りより細かく
            // 周波数を推定する
            // ========================================================

            double left = scores[bestBin - 1];


            double center = scores[bestBin];


            double right = scores[bestBin + 1];


            double correction = 0;


            if (!double.IsInfinity(left) && !double.IsInfinity(center) && !double.IsInfinity(right))
            {
                double denominator = left - 2.0 * center + right;


                if (Math.Abs(denominator) > 0.0000000001)
                {
                    correction = 0.5 * (left - right) / denominator;
                }
            }


            correction = Math.Max(-0.5,Math.Min(0.5,correction));


            // ========================================================
            // FFT位置
            //
            // ↓
            //
            // Hz
            // ========================================================

            double exactBin = bestBin + correction;


            double frequency = exactBin * sampleRate / FftSize;


            // ========================================================
            // 異常値
            // ========================================================

            if (frequency < 70 || frequency > 1400)
            {
                return null;
            }


            // ========================================================
            // 周波数を滑らかにする
            // ========================================================

            frequency = SmoothFrequency(frequency);


            // ========================================================
            // 周波数
            //
            // ↓
            //
            // 音名・cent
            // ========================================================

            ConvertFrequencyToNote(frequency,out string noteName,out double cents);


            // ========================================================
            // 結果
            // ========================================================

            return new TunerResult
            {
                frequency = frequency,

                note = noteName,

                cents = cents,

                InputChannel = inputChannel,

                SampleRate = sampleRate,

                hasSignal = hassignal
            };
        }



        // ============================================================
        // RMS
        // ============================================================

        private double CalculateRms(float[] samples)
        {
            double sum = 0;


            for (int i = 0;i < samples.Length;i++)
            {
                double value = samples[i];


                sum +=  value * value;
            }


            return Math.Sqrt(sum / samples.Length);
        }



        // ============================================================
        // 周波数を滑らかにする
        // ============================================================

        private double SmoothFrequency(double frequency)
        {
            if (_smoothedFrequency == null)
            {
                _smoothedFrequency = frequency;


                return frequency;
            }


            double differenceCents = 1200.0 * Log2(frequency / _smoothedFrequency.Value);


            // 大きく周波数が変化した
            // → 別の音と判断
            if (Math.Abs(differenceCents) > 150)
            {
                _smoothedFrequency = frequency;
            }
            else
            {
                // 少しずつ滑らかに動かす
                const double alpha = 0.30;


                _smoothedFrequency = _smoothedFrequency.Value * (1.0 - alpha) + frequency * alpha;
            }


            return _smoothedFrequency.Value;
        }



        // ============================================================
        // 周波数
        //
        // ↓
        //
        // 音名
        //
        // ↓
        //
        // cent
        // ============================================================

        private void ConvertFrequencyToNote(double frequency,out string noteName,out double cents)
        {
            // ========================================================
            // MIDIノート番号へ変換
            //
            // A4 = MIDI 69
            // ========================================================

            double midiNote = 69.0 + 12.0 * Log2(frequency / _referencePitch);


            int nearestMidi = (int)Math.Round(midiNote);


            // ========================================================
            // cent
            // ========================================================

            cents = (midiNote - nearestMidi) * 100.0;


            // ========================================================
            // 音名
            // ========================================================

            string[] noteNames =
            {
                "C",
                "C#",
                "D",
                "D#",
                "E",
                "F",
                "F#",
                "G",
                "G#",
                "A",
                "A#",
                "B"
            };


            int noteIndex = (nearestMidi % 12 + 12) % 12;


            int octave = nearestMidi / 12 - 1;


            noteName = noteNames[noteIndex] + octave;
        }



        // ============================================================
        // FFT
        //
        // Cooley-Tukey FFT
        //
        // NAudioを使用しない
        // ============================================================

        private void FFT(Complex[] buffer)
        {
            int n = buffer.Length;


            // ========================================================
            // ビット反転
            // ========================================================

            int j = 0;


            for (int i = 1;i < n;i++)
            {
                int bit = n >> 1;


                while ((j & bit) != 0)
                {
                    j ^= bit;


                    bit >>= 1;
                }


                j ^= bit;


                if (i < j)
                {
                    Complex temp = buffer[i];


                    buffer[i] = buffer[j];


                    buffer[j] = temp;
                }
            }


            // ========================================================
            // FFT本体
            // ========================================================

            for (int length = 2;length <= n;length <<= 1)
            {
                double angle = -2.0 * Math.PI / length;


                Complex wLength = new Complex(Math.Cos(angle),Math.Sin(angle));


                for (int i = 0;i < n;i += length)
                {
                    Complex w = Complex.One;


                    int halfLength = length / 2;


                    for (int k = 0;k < halfLength;k++)
                    {
                        Complex even = buffer[i + k];


                        Complex odd = buffer[i + k + halfLength] * w;


                        buffer[i + k] = even + odd;


                        buffer[i + k + halfLength] = even - odd;


                        w *= wLength;
                    }
                }
            }
        }



        // ============================================================
        // Log2 = そこが2の対数を計算するメソッド(周波数をcentやMIDIノート番号に変換する時に使用する)
        // ============================================================

        private double Log2(double value)
        {
            return Math.Log(value) / Math.Log(2.0);
        }



        // ============================================================
        // リセット
        // ============================================================

        public void Reset()
        {
            Array.Clear(_sampleBuffer,0,_sampleBuffer.Length);


            _bufferCount = 0;


            _smoothedFrequency = null;
        }
    }
}
