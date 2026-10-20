using System;

namespace FXForge.AudioEffects;

// RBJ Audio EQ Cookbookの式を用いたDirect Form II Transposed。
// 係数の計算は設定変更時のみ。Processは音声スレッドでのみ呼び出す。
internal sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

    public double Process(double x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        if (!double.IsFinite(y)) { Reset(); return 0; }
        return y;
    }

    public void Reset() => _z1 = _z2 = 0;
    private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
    { _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0; }

    public void LowPass(double sampleRate, double frequency, double q = .7071067811865476)
    {
        double w = 2 * Math.PI * Math.Clamp(frequency, 10, sampleRate * .45) / sampleRate;
        double c = Math.Cos(w), alpha = Math.Sin(w) / (2 * q);
        Set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }
    public void HighPass(double sampleRate, double frequency)
    {
        double w = 2 * Math.PI * Math.Clamp(frequency, 10, sampleRate * .45) / sampleRate;
        double c = Math.Cos(w), alpha = Math.Sin(w) / Math.Sqrt(2);
        Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }
    public void Peak(double rate, double frequency, double gainDb, double q)
    {
        double w = 2 * Math.PI * frequency / rate, c = Math.Cos(w);
        double alpha = Math.Sin(w) / (2 * q), a = Math.Pow(10, gainDb / 40);
        Set(1 + alpha * a, -2 * c, 1 - alpha * a,
            1 + alpha / a, -2 * c, 1 - alpha / a);
    }
    public void Shelf(double rate, double frequency, double gainDb, bool high)
    {
        double w = 2 * Math.PI * frequency / rate, c = Math.Cos(w);
        double a = Math.Pow(10, gainDb / 40);
        double t = Math.Sin(w) * Math.Sqrt(2 * a); // shelf slope S=1
        if (high)
            Set(a * ((a + 1) + (a - 1) * c + t),
                -2 * a * ((a - 1) + (a + 1) * c),
                a * ((a + 1) + (a - 1) * c - t),
                (a + 1) - (a - 1) * c + t,
                2 * ((a - 1) - (a + 1) * c),
                (a + 1) - (a - 1) * c - t);
        else
            Set(a * ((a + 1) - (a - 1) * c + t),
                2 * a * ((a - 1) - (a + 1) * c),
                a * ((a + 1) - (a - 1) * c - t),
                (a + 1) + (a - 1) * c + t,
                -2 * ((a - 1) + (a + 1) * c),
                (a + 1) + (a - 1) * c - t);
    }
}
