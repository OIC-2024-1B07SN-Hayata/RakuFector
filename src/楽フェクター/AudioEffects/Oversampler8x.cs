using System;

namespace FXForge.AudioEffects;

/// <summary>63tap半帯域FIRの2倍変換を3段重ねた8倍オーバーサンプリング。</summary>
internal sealed class Oversampler8x
{
    public const int Factor = 8;
    private readonly HalfBand2x _first = new(), _second = new(), _third = new();
    public double[] Samples { get; } = new double[Factor];

    public void Up(double input)
    {
        _first.Up(input, out double a, out double b);
        _second.Up(a, out double aa, out double ab);
        _third.Up(aa, out Samples[0], out Samples[1]);
        _third.Up(ab, out Samples[2], out Samples[3]);
        _second.Up(b, out double ba, out double bb);
        _third.Up(ba, out Samples[4], out Samples[5]);
        _third.Up(bb, out Samples[6], out Samples[7]);
    }
    public double Down()
    {
        double a = _third.Down(Samples[0], Samples[1]);
        double b = _third.Down(Samples[2], Samples[3]);
        double c = _third.Down(Samples[4], Samples[5]);
        double d = _third.Down(Samples[6], Samples[7]);
        return _first.Down(_second.Down(a, b), _second.Down(c, d));
    }
    public void Reset() { _first.Reset(); _second.Reset(); _third.Reset(); Array.Clear(Samples); }

    private sealed class HalfBand2x
    {
        // 奇数側の係数は中央の0.5以外ゼロ。偶数側だけ計算するポリフェーズ実装。
        private static readonly double[] Even = MakeCoefficients();
        private readonly double[] _up = new double[32], _even = new double[32], _odd = new double[32];
        private int _upHead, _downHead;
        private static double[] MakeCoefficients()
        {
            var result = new double[32];
            double sum = 0;
            for (int k = 0; k < result.Length; k++)
            {
                int n = 2 * k;
                double offset = n - 31;
                double window = .42 - .5 * Math.Cos(2 * Math.PI * n / 62) + .08 * Math.Cos(4 * Math.PI * n / 62);
                result[k] = Math.Sin(Math.PI * .5 * offset) / (Math.PI * offset) * window;
                sum += result[k];
            }
            for (int k = 0; k < result.Length; k++) result[k] *= .5 / sum;
            return result;
        }
        public void Up(double input, out double a, out double b)
        {
            _upHead = (_upHead + 1) & 31;
            _up[_upHead] = input;
            double sum = 0;
            for (int k = 0; k < 32; k++) sum += Even[k] * _up[(_upHead - k) & 31];
            a = 2 * sum;
            b = _up[(_upHead - 15) & 31];
        }
        public double Down(double even, double odd)
        {
            _downHead = (_downHead + 1) & 31;
            _even[_downHead] = even; _odd[_downHead] = odd;
            double sum = .5 * _even[(_downHead - 15) & 31];
            for (int k = 0; k < 32; k++) sum += Even[k] * _odd[(_downHead - k) & 31];
            return sum;
        }
        public void Reset() { Array.Clear(_up); Array.Clear(_even); Array.Clear(_odd); _upHead = _downHead = 0; }
    }
}
