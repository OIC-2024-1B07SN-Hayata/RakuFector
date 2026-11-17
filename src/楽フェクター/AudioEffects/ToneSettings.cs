
namespace FXForge.AudioEffects;

public enum DriveKind
{
    Sd1,
    Rat
}

// ========================================
// 歪みエフェクト設定
// ========================================

public sealed record DriveSettings(
    DriveKind Kind,
    bool Enabled,
    double Drive,
    double Tone,
    double Level
);

// ========================================
// アンプ設定
// ========================================

public sealed record AmpSettings(
    bool Enabled,
    double Gain,
    double Bass,
    double Middle,
    double Treble,
    double Level
);

// ========================================
// 音作り全体の設定
// ========================================

public sealed record ToneSettings(
    DriveSettings A,
    DriveSettings B,
    bool ReverseDrives,
    AmpSettings Amp,
    double OutputLevel
)
{
    public static ToneSettings Default { get; } = new(
        // ========================================
        // SD-1
        // ========================================
        new(
            DriveKind.Sd1,
            true,
            .64,  // DRIVE
            .52,  // TONE
            .52   // LEVEL
        ),

        // ========================================
        // RAT
        // ========================================
        new(
            DriveKind.Rat,
            false,
            .75,  // DRIVE
            .36,  // FILTER
            .52   // LEVEL
        ),

        // エフェクト順序
        false,

        // ========================================
        // AMP：音量改善
        // ========================================
        new(
            true,
            .25,  // GAIN：0.50 → 0.25
            .50,  // BASS
            .50,  // MIDDLE
            .50,  // TREBLE
            .80   // LEVEL：変更なし
        ),

        // ========================================
        // 全体出力
        // ========================================
        .65
    );
}
