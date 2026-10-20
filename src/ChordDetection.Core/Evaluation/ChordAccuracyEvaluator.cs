using System.Globalization;
using ChordDetection.Core.Chords;

namespace ChordDetection.Core.Evaluation;

/// <summary>A reference ("correct") chord label.</summary>
public sealed record ChordLabel(double StartSeconds, double EndSeconds, ChordResult Chord);

/// <param name="Accuracy">Correct time / labelled time (0..1). NaN if nothing is labelled.</param>
/// <param name="LabelledSeconds">Total length of reference labels other than N.C.</param>
/// <param name="CorrectSeconds">Time during which root and quality match.</param>
public sealed record AccuracyReport(double Accuracy, double LabelledSeconds, double CorrectSeconds, DisplayLevel Level)
{
    public override string ToString() => double.IsNaN(Accuracy)
        ? "正解ラベルがありません"
        : $"一致率 {Accuracy:P1}（{CorrectSeconds:0.0} / {LabelledSeconds:0.0} 秒, {Level}）";
}

/// <summary>
/// Time-weighted chord accuracy. Both reference and estimate are simplified to the same level,
/// then root + quality are compared every 10 ms (bass is ignored).
/// </summary>
public sealed class ChordAccuracyEvaluator
{
    private const double Step = 0.01;
    private readonly ChordSimplifier _simplifier;

    public ChordAccuracyEvaluator(ChordSimplifier? simplifier = null)
    {
        _simplifier = simplifier ?? new ChordSimplifier();
    }

    public AccuracyReport Evaluate(
        IReadOnlyList<ChordLabel> reference, IReadOnlyList<DetectedChord> estimated, DisplayLevel level)
    {
        double labelled = 0, correct = 0;
        foreach (var label in reference)
        {
            if (label.Chord.IsNoChord) continue;
            string expected = _simplifier.Simplify(label.Chord, level).NameWithoutBass;

            for (double t = label.StartSeconds + Step / 2; t < label.EndSeconds; t += Step)
            {
                labelled += Step;
                var hit = estimated.FirstOrDefault(e => e.StartSeconds <= t && t < e.EndSeconds);
                if (hit is not null && _simplifier.Simplify(hit.Chord, level).NameWithoutBass == expected)
                    correct += Step;
            }
        }
        return new AccuracyReport(labelled > 0 ? correct / labelled : double.NaN, labelled, correct, level);
    }

    /// <summary>
    /// Reads labels from CSV lines "start,end,chord" (seconds). Lines starting with # and a header are skipped.
    /// </summary>
    public static IReadOnlyList<ChordLabel> ParseLabels(IEnumerable<string> lines, ChordNameParser? parser = null)
    {
        parser ??= new ChordNameParser();
        var labels = new List<ChordLabel>();
        int lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cols = line.Split(',', StringSplitOptions.TrimEntries);
            if (cols.Length < 3) throw new FormatException($"{lineNo}行目: 「開始,終了,コード」の3列が必要です。");
            if (!double.TryParse(cols[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
            {
                if (lineNo == 1) continue; // header
                throw new FormatException($"{lineNo}行目: 開始時刻が数値ではありません。");
            }
            if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                throw new FormatException($"{lineNo}行目: 終了時刻が数値ではありません。");
            if (!parser.TryParse(cols[2], out var chord))
                throw new FormatException($"{lineNo}行目: コード名 '{cols[2]}' を解釈できません。");
            labels.Add(new ChordLabel(start, end, chord));
        }
        return labels;
    }
}
