namespace ChordDetection.App.ViewModels;

/// <summary>One chord in a bar of the chord sheet (a whole bar or one half).</summary>
/// <param name="Name">Sounding chord name spelled for the key; "%" when the bar repeats the previous one.</param>
/// <param name="Sub">Degree below the name (ディグリーを表示); empty otherwise.</param>
/// <param name="ShapeText">Shape to fret with the capo, e.g. "(G)"; empty without a capo.</param>
public sealed record BarSlotViewModel(string Name, string Sub, string ShapeText)
{
    public bool HasSub => Sub.Length > 0;
}

/// <summary>One bar of the chord sheet: | C | Am | F G |.</summary>
public sealed class BarRowViewModel
{
    public BarRowViewModel(int number, double startSeconds, IReadOnlyList<BarSlotViewModel> slots, string toolTip)
    {
        Number = number;
        StartSeconds = startSeconds;
        Slots = slots;
        ToolTip = toolTip;
    }

    /// <summary>Bar number; bar 1 is the first bar of the metronome / drums.</summary>
    public int Number { get; }

    public double StartSeconds { get; }

    /// <summary>One slot for the whole bar, or two for the halves; one "N.C." slot when nothing was played.</summary>
    public IReadOnlyList<BarSlotViewModel> Slots { get; }

    public string ToolTip { get; }
}
