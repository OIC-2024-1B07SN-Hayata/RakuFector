using System;

namespace FXForge.AudioEffects;

public static class DriveCurves
{
    public static double Sd1(double input) => SoftKnee(input,
        input >= 0 ? DriveVoicing.Sd1.PositiveCeiling : DriveVoicing.Sd1.NegativeCeiling,
        DriveVoicing.Sd1.KneeWidth);

    public static double Rat(double input) => SoftKnee(input,
        DriveVoicing.Rat.ClipCeiling, DriveVoicing.Rat.KneeWidth);

    private static double SoftKnee(double input, double ceiling, double knee)
    {
        double start = ceiling - knee, magnitude = Math.Abs(input);
        double clipped = magnitude <= start ? magnitude : start + knee * Math.Tanh((magnitude - start) / knee);
        return Math.CopySign(clipped, input);
    }
}
