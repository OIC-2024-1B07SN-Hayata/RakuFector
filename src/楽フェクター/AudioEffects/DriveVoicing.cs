
namespace FXForge.AudioEffects;

/// <summary>
/// SD-1 / RAT 音色パラメーター。
/// RATは実機回路の完全再現ではなく、調整用DSPモデル。
/// </summary>
public static class DriveVoicing
{
    public static class Sd1
    {
        // SD-1は変更しない
        public const double GainMinimum = 1.5;
        public const double GainRange = 20.0;
        public const double PositiveCeiling = 0.70;
        public const double NegativeCeiling = 0.55;
        public const double KneeWidth = 0.20;
        public const double CleanBlend = 0.30;
        public const double InputHighPassHz = 145;
        public const double InputLowPassHz = 6500;
        public const double BodyHz = 150;
        public const double BodyDb = 0.8;
        public const double MidHz = 850;
        public const double MidDb = 2.0;
        public const double ToneMinHz = 1600;
        public const double ToneMaxHz = 7000;
        public const double LevelScale = 0.5;
    }

    public static class Rat
    {
        // ====================================
        // 1. 歪み前の低域整理
        // ====================================

        public const double InputHighPassHz = 115.0;
        public const double InputLowPassHz = 10500.0;

        // 歪み前に低音を少し削る
        public const double PreBassHz = 180.0;
        public const double PreBassDb = -3.0;

        // 歪み前に中音域を強調
        public const double PreMidHz = 780.0;
        public const double PreMidDb = 4.0;
        public const double PreMidQ = 0.85;

        // ====================================
        // 2. メインの歪み
        // ====================================

        public const double GainMinimum = 70.0;
        public const double GainRange = 400.0;

        public const double ClipCeiling = 0.40;
        public const double KneeWidth = 0.07;

        // ====================================
        // 3. 弱い入力音への増幅補助
        // ====================================

        // 1.0にすれば増幅補助を無効化
        public const double MaxSingleNoteBoost = 2.0;

        public const double AutoBoostTargetRmsLike = 0.015;

        public const double EnvelopeAttackMs = 5.0;
        public const double EnvelopeReleaseMs = 100.0;

        // ====================================
        // 4. 第2クリッピング
        // ====================================

        public const double SecondHighPassHz = 130.0;
        public const double SecondLowPassHz = 7200.0;

        public const double SecondMidHz = 1000.0;
        public const double SecondMidDb = 2.0;
        public const double SecondMidQ = 0.85;

        // 第2段の歪みの強さ
        public const double SecondStageGainMin = 2.0;
        public const double SecondStageGainRange = 1.6;

        // わずかな非対称性を加える
        public const double SecondStageBias = 0.04;

        // 第2段の混合率
        // 0.0なら無効、1.0なら第2段の信号のみ
        public const double SecondStageMix = 0.70;

        // ====================================
        // 5. 歪み後のEQ
        // ====================================

        public const double BodyHz = 180.0;
        public const double BodyDb = -1.0;

        public const double MidHz = 750.0;
        public const double MidDb = 2.5;

        // ====================================
        // 6. FILTER
        // ====================================

        public const double FilterMinHz = 2400.0;
        public const double FilterMaxHz = 8500.0;

        // ====================================
        // 7. Drive連動EQ
        // ====================================

        public const double DriveBassHz = 180.0;
        public const double DriveBassBoostDb = 0.25;

        public const double DriveMidHz = 750.0;
        public const double DriveMidBoostDb = 1.75;
        public const double DriveMidQ = 0.75;

        // ====================================
        // 8. 出力音量
        // ====================================

        public const double LevelScale = 0.10;

        // ====================================
        // 9. 旧版との互換性用
        // 以下の並列歪み設定は新版では未使用
        // ====================================

        public const double MidBandHighPassHz = 170;
        public const double MidBandLowPassHz = 1900;
        public const double MidClipDriveScale = 1.0;
        public const double MidClipMix = 0.0;
        public const double MidClipPostHz = 700;
        public const double MidClipPostDb = 2.0;
        public const double MidClipPostQ = 0.80;

        public const double GritSplitHz = 1100;
        public const double GritGain = 2.0;
        public const double GritMix = 0.0;
        public const double GritPostHighPassHz = 850;
        public const double EdgePreEmphasis = 0.35;
    }
}
