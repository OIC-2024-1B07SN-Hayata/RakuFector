namespace FXForge.AudioEffects;

public enum DriveKind { Sd1, Rat }

// UIが作成し、音声スレッドへ1つの参照として渡す設定。値は0～1。
public sealed record DriveSettings(DriveKind Kind, bool Enabled, double Drive,
    double Tone, double Level);
public sealed record AmpSettings(bool Enabled, double Gain, double Bass,
    double Middle, double Treble, double Level);
public sealed record ToneSettings(DriveSettings A, DriveSettings B, bool ReverseDrives,
    AmpSettings Amp, double OutputLevel)
{
    public static ToneSettings Default { get; } = new(
        new(DriveKind.Sd1, true, .64, .52, .52),
        new(DriveKind.Rat, false, .75, .36, .52),
        false, new(true, .08, .50, .50, .50, .65), .65);
}
