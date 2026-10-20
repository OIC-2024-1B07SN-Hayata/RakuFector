namespace FXForge.AudioEffects;

/// <summary>ご希望に合わせた独自の音色。SD-1の基準を前版RAT付近へ移し、RATをさらに強くしています。</summary>
public static class DriveVoicing
{
    public static class Sd1
    {
        // 前版のRATと同じゲイン・入力帯域・低域整形を土台にする。
        public const double InputHighPassHz = 65;
        public const double InputLowPassHz = 11500;
        public const double GainMinimum = 3;
        public const double GainRange = 95;
        public const double PositiveCeiling = .48;
        public const double NegativeCeiling = .44;
        public const double KneeWidth = .055;
        public const double CleanBlend = .02;
        public const double BodyHz = 135;
        public const double BodyDb = 3.8;
        public const double MidHz = 850;
        public const double MidDb = -1.2;
        public const double ToneMinHz = 650;
        public const double ToneMaxHz = 10500;
        public const double LevelScale = 1.35;
    }
    public static class Rat
    {
        public const double InputHighPassHz = 75;
        public const double InputLowPassHz = 14500;
        public const double GainMinimum = 2;
        public const double GainRange = 160;
        public const double ClipCeiling = .38;
        public const double KneeWidth = .005;
        public const double GritSplitHz = 1400;
        public const double GritGain = 2.5;
        public const double GritMix = .22; // 中高域を別に深く歪ませ、ざらつきを足す。
        public const double BodyHz = 135;
        public const double BodyDb = 8.5;
        public const double MidHz = 1300;
        public const double MidDb = 1.5;
        public const double FilterMinHz = 800;
        public const double FilterMaxHz = 13500;
        public const double LevelScale = 1.35;
    }
}
