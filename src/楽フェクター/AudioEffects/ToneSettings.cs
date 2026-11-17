namespace FXForge.AudioEffects;

public enum DriveKind
{
    Sd1 = 0,
    Rat = 1,
    Ts9 = 2,
    Bd2 = 3,
    Ds1 = 4
}

public sealed record DriveSettings(
    DriveKind Kind,
    bool Enabled,
    double Drive,
    double Tone,
    double Level
);

public sealed record AmpSettings(
    bool Enabled,
    double Gain,
    double Bass,
    double Middle,
    double Treble,
    double Level
);

public sealed record ToneSettings(
    DriveSettings A,
    DriveSettings B,
    bool ReverseDrives,
    AmpSettings Amp,
    double OutputLevel
)
{
    public static ToneSettings Default { get; } = new(
        new DriveSettings(
            DriveKind.Sd1,
            true,
            .64,
            .52,
            .52),
        new DriveSettings(
            DriveKind.Rat,
            false,
            .75,
            .36,
            .52),
        false,
        new AmpSettings(
            true,
            .25,
            .50,
            .50,
            .50,
            .80),
        .65
    );
}