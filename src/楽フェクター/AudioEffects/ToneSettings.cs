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
)
{
    public double High { get; init; } = .50;
}

public enum DelayMode
{
    Digital = 0,
    Analog = 1,
    Tape = 2
}

public enum DelayNote
{
    Manual = 0,
    Sixteenth = 1,
    EighthTriplet = 2,
    Eighth = 3,
    DottedEighth = 4,
    Quarter = 5,
    Half = 6
}

public sealed record DelaySettings(
    bool Enabled,
    DelayMode Mode,
    double TimeMilliseconds,
    double Feedback,
    double Mix)
{
    // 反復音だけに適用する音量。0.0～1.0。
    // 初期値1.0で、従来の音量を維持する。
    public double WetLevel { get; init; } = 1.0;

    public int TempoBpm { get; init; } = 120;

    // ManualならTIMEモード。
    // それ以外ならTEMPOモード。
    public DelayNote Note { get; init; } = DelayNote.Manual;

    // TimeMillisecondsにはTIMEモードの設定を保持する。
    // DSPではこちらの実効時間を使用する。
    public double EffectiveTimeMilliseconds
    {
        get
        {
            double time = Note == DelayNote.Manual
                ? TimeMilliseconds
                : CalculateTime(TempoBpm, Note);

            if (!double.IsFinite(time) ||
                time < MinimumTimeMs ||
                time > MaximumTimeMs)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(TimeMilliseconds),
                    "Delay Timeは20～2000msの範囲にしてください。");
            }

            return time;
        }
    }

    public static double CalculateTime(
        int bpm,
        DelayNote note)
    {
        if (bpm is < 30 or > 300)
            throw new ArgumentOutOfRangeException(nameof(bpm));

        double beats = note switch
        {
            DelayNote.Sixteenth => .25,
            DelayNote.EighthTriplet => 1.0 / 3.0,
            DelayNote.Eighth => .5,
            DelayNote.DottedEighth => .75,
            DelayNote.Quarter => 1,
            DelayNote.Half => 2,

            _ => throw new ArgumentOutOfRangeException(nameof(note))
        };

        return 60000.0 / bpm * beats;
    }

    public static string NoteLabel(DelayNote note) => note switch
    {
        DelayNote.Manual => "TIME",
        DelayNote.Sixteenth => "16分",
        DelayNote.EighthTriplet => "8分3連",
        DelayNote.Eighth => "8分",
        DelayNote.DottedEighth => "付点8分",
        DelayNote.Quarter => "4分",
        DelayNote.Half => "2分",
        _ => "不明"
    };

    public const double MinimumTimeMs = 20;
    public const double MaximumTimeMs = 2000;
    public const double MaximumFeedback = .95;

    public static DelaySettings Default { get; } =
        new(false, DelayMode.Digital, 400, .35, .25);
}

public sealed record ToneSettings(
    DriveSettings A,
    DriveSettings B,
    bool ReverseDrives,
    AmpSettings Amp,
    double OutputLevel
)
{
    public DelaySettings Delay { get; init; } =
        DelaySettings.Default;

    // 残響を引き継ぐための音色読込番号。DBには保存しない。
    public long DelayRevision { get; init; }

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
            .80)
        {
            High = .50
        },
        .65
    );
}