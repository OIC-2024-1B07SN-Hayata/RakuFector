using System;

namespace FXForge.AudioEffects;

// 1次ADAA: 2サンプルの間を補間した信号のクリップ値を積分して平均する。
// 鋭いクリップの性質を保ちながら、不要な折り返しを減らす。8倍処理内で使用。
internal sealed class AntialiasedRatClipper
{
    private double _previousInput, _previousIntegral;
    public double Process(double input)
    {
        double integral = Integral(input), delta = input - _previousInput;
        double result = Math.Abs(delta) < 1e-6 * Math.Max(1, Math.Abs(input))
            ? DriveCurves.Rat((input + _previousInput) * .5)
            : (integral - _previousIntegral) / delta;
        _previousInput = input; _previousIntegral = integral;
        return Math.Clamp(result, -DriveVoicing.Rat.ClipCeiling, DriveVoicing.Rat.ClipCeiling);
    }
    private static double Integral(double input)
    {
        double magnitude = Math.Abs(input), knee = DriveVoicing.Rat.KneeWidth;
        double start = DriveVoicing.Rat.ClipCeiling - knee;
        if (magnitude <= start) return .5 * magnitude * magnitude;
        double distance = magnitude - start, u = distance / knee;
        // log(cosh(u))をオーバーフローさせずに計算。
        double logCosh = u + Math.Log(1 + Math.Exp(-2 * u)) - .6931471805599453;
        return .5 * start * start + start * distance + knee * knee * logCosh;
    }
    public void Reset() { _previousInput = 0; _previousIntegral = 0; }
}
