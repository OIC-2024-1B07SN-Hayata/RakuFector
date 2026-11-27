using ChordDetection.Core.Chords;
using ChordDetection.Core.Music;
using ChordDetection.Core.Models;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); passed++; Console.WriteLine("PASS " + label); }

var catalog = ChordCatalog.Default;
ChordResult Chord(int root, string symbol = "") => new(root, catalog[symbol], null, 1);
DisplayedChord At(double start, double end, ChordResult chord) => new(start, end, chord);
string Sheet(IReadOnlyList<BarChords> bars) =>
    string.Join(" | ", bars.Select(b => b.IsEmpty ? "N.C." : string.Join(" ", b.Chords.Select(c => c.Name))));

var C = Chord(0); var Am = Chord(9, "m"); var F = Chord(5); var G = Chord(7);
var noChord = new ChordResult(0, null, null, 0);

// ---------- BarGrid ----------
var grid = new BarGrid(120, 4); // 2 s per bar
Check(Math.Abs(grid.BarSeconds - 2) < 1e-9, "120 BPM 4/4 bar is 2 s");
Check(grid.BarStart(3) == 6, "bar 4 starts at 6 s");
Check(grid.BarIndexAt(5.99) == 2 && grid.BarIndexAt(6) == 3, "bar index at boundary");
Check(grid.BarCount(7) == 4 && grid.BarCount(8) == 4 && grid.BarCount(0) == 0, "bar count covers the audio");
var shifted = new BarGrid(90, 3, 0.08);
Check(Math.Abs(shifted.BarSeconds - 2) < 1e-9 && Math.Abs(shifted.BarStart(1) - 2.08) < 1e-9, "offset and 3/4");
Check(Throws(() => new BarGrid(0, 4)) && Throws(() => new BarGrid(120, 0)), "invalid tempo rejected");
Check(grid.NearestBarStart(2.9) == 2 && grid.NearestBarStart(3.1) == 4 && grid.NearestBarStart(0.4) == 0, "nearest bar line");
Check(Math.Abs(shifted.NearestBarStart(1.5) - 2.08) < 1e-9 && Math.Abs(shifted.NearestBarStart(0.5) - 0.08) < 1e-9, "nearest bar line with offset");
Check(Math.Abs(new BarGrid(120, 4, 0.5).NearestBarStart(-0.6) - (-1.5)) < 1e-9, "nearest bar line before bar 1");
Check(grid.NearestLine(1.2, 4) == 1.0 && grid.NearestLine(1.3, 4) == 1.5, "nearest beat");
Check(grid.NearestLine(0.13, 16) == 0.125 && grid.NearestLine(0.06, 16) == 0, "nearest 16th note");
Check(Math.Abs(new BarGrid(120, 4, 0.1).NearestLine(1.2, 4) - 1.1) < 1e-9, "nearest beat with offset");
Check(Throws(() => grid.NearestLine(1, 0)), "no lines rejected");

// ---------- BarChordQuantizer ----------
var q = new BarChordQuantizer();

// One chord per bar, played slightly early / late (human timing): still one chord per bar.
var simple = new[] { At(0.0, 2.1, C), At(2.1, 3.9, Am), At(3.9, 6.05, F), At(6.05, 8, G) };
Check(Sheet(q.Quantize(simple, grid, 8)) == "C | Am | F | G", "one chord per bar despite timing");

// Two chords in one bar → halves, in playing order.
var split = new[] { At(0, 2, C), At(2, 3, F), At(3, 4, G), At(4, 6, C) };
Check(Sheet(q.Quantize(split, grid, 6)) == "C | F G | C", "bar split into halves");

// A short passing chord (< 35 %) does not split the bar.
var passing = new[] { At(0, 1.6, C), At(1.6, 2, G) };
Check(Sheet(q.Quantize(passing, grid, 2)) == "C", "passing chord ignored");

// Order follows time even when the second chord is longer.
var lateLonger = new[] { At(0, 0.8, Am), At(0.8, 2, F) };
Check(Sheet(q.Quantize(lateLonger, grid, 2)) == "Am F", "halves ordered by time");

// Silent bars in the middle are N.C.; leading / trailing silence is trimmed but numbers are kept.
var gaps = new[] { At(2, 4, C), At(6, 8, G), At(8, 8.2, noChord) };
var trimmed = q.Quantize(gaps, grid, 12);
Check(Sheet(trimmed) == "C | N.C. | G", "inner silence N.C., edges trimmed");
Check(trimmed[0].Number == 2 && trimmed[^1].Number == 4, "bar numbers kept after trimming");
Check(q.Quantize(gaps, grid, 12, trimEmptyEdges: false).Count == 6, "untrimmed keeps every bar");

// Barely covered bar (< 25 %) is N.C.
Check(q.Quantize([At(0, 0.3, C)], grid, 2).Count == 0, "barely covered bar is N.C.");

// Same chord split into several pieces is added up.
// C (0.8 + 0.7 = 1.5 s) wins over G (0.5 s, under 35 %), although it is split in two pieces.
Check(Sheet(q.Quantize([At(0, 0.8, C), At(0.8, 1.3, G), At(1.3, 2, C)], grid, 2)) == "C", "summed chord wins, short one ignored");

// Bars before the offset are not produced (the recording started before bar 1 was heard).
var late = new BarGrid(120, 4, 0.5);
var lateBars = q.Quantize([At(0, 2.5, C), At(2.5, 4.5, G)], late, 4.5);
Check(Sheet(lateBars) == "C | G" && Math.Abs(lateBars[0].StartSeconds - 0.5) < 1e-9, "offset shifts the bars");

Check(q.Quantize([], grid, 10).Count == 0, "no chords → no bars");

// ---------- DegreeNamer ----------
string Degrees(MusicKey key, MinorDegreeBase minorBase, params ChordResult[] chords) =>
    string.Join(" ", chords.Select(c => DegreeNamer.Degree(c, key, minorBase)));
var keyC = new MusicKey(0, false);
var keyAm = new MusicKey(9, true);
const MinorDegreeBase relative = MinorDegreeBase.RelativeMajor, tonic = MinorDegreeBase.MinorTonic;
var E7 = Chord(4, "7");

Check(Degrees(keyC, relative, C, Chord(2, "m7"), Chord(7, "7"), Am, Chord(11, "m7b5")) == "Ⅰ Ⅱm7 Ⅴ7 Ⅵm Ⅶm7b5",
    "diatonic degrees in C");
Check(Degrees(keyC, relative, Chord(10), Chord(6), Chord(3, "M7"), Chord(8)) == "♭Ⅶ ♯Ⅳ ♭ⅢM7 ♭Ⅵ", "borrowed chords in C");
Check(DegreeNamer.Degree(new ChordResult(0, catalog[""], 4, 1), keyC) == "Ⅰ/Ⅲ", "slash bass as degree");
Check(DegreeNamer.Degree(new ChordResult(2, catalog[""], 6, 1), keyC) == "Ⅱ/♯Ⅳ", "slash bass outside the scale");
Check(Degrees(keyAm, relative, Am, F, G, C, E7) == "Ⅵm Ⅳ Ⅴ Ⅰ Ⅲ7", "minor key: relative major is Ⅰ (default)");
Check(Degrees(keyAm, tonic, Am, F, G, C, E7) == "Ⅰm ♭Ⅵ ♭Ⅶ ♭Ⅲ Ⅴ7", "minor key: minor tonic is Ⅰ");
Check(Degrees(keyC, tonic, C, G) == "Ⅰ Ⅴ", "minor base does not change a major key");
Check(Degrees(new MusicKey(2, false), relative, Chord(2), Chord(11, "m"), Chord(7), Chord(9)) == "Ⅰ Ⅵm Ⅳ Ⅴ",
    "transposed progression keeps its degrees");
Check(DegreeNamer.Degree(noChord, keyC) == ChordResult.NoChordName, "N.C. stays N.C.");

// ---------- Scale / Fretboard ----------
bool SameSet(IReadOnlySet<int> set, params int[] expected) => set.SetEquals(expected);
Check(SameSet(Scale.PitchClassesOf(keyC, ScaleType.Major), 0, 2, 4, 5, 7, 9, 11), "C major scale");
Check(SameSet(Scale.PitchClassesOf(keyAm, ScaleType.NaturalMinor), 0, 2, 4, 5, 7, 9, 11)
    && Scale.RootFor(keyAm, ScaleType.NaturalMinor) == 9, "A natural minor: same notes, root A");
Check(Scale.RootFor(keyC, ScaleType.MinorPentatonic) == 9
    && SameSet(Scale.PitchClassesOf(keyC, ScaleType.MinorPentatonic), 9, 0, 2, 4, 7), "key C, minor pentatonic → A minor pentatonic");
Check(Scale.RootFor(keyAm, ScaleType.MajorPentatonic) == 0, "key Am, major pentatonic → C major pentatonic");
Check(Scale.PitchClassesOf(keyAm, ScaleType.Blues).Contains(3) && Scale.PitchClassesOf(keyAm, ScaleType.Blues).Count == 6, "blues has the ♭5");
Check(Scale.DefaultFor(keyC) == ScaleType.Major && Scale.DefaultFor(keyAm) == ScaleType.NaturalMinor, "default scale follows the key");
Check(Scale.IntervalName(3) == "♭3" && Scale.IntervalName(7) == "5" && Scale.IntervalName(12) == "R", "interval names");

Check(Fretboard.PitchClassAt(5, 0) == 4 && Fretboard.PitchClassAt(5, 5) == 9 && Fretboard.PitchClassAt(0, 12) == 4, "fretboard pitch classes");

var amScale = Scale.PitchClassesOf(keyAm, ScaleType.NaturalMinor);
var plain = Fretboard.Marks(amScale, 9, null);
Check(plain.All(m => m.Kind is FretMarkKind.ScaleNote or FretMarkKind.ScaleRoot), "no chord → scale dots only");
Check(plain.Single(m => m.StringIndex == 5 && m.Fret == 5).Kind == FretMarkKind.ScaleRoot, "6th string 5th fret is the root A");
Check(plain.Any(m => m.StringIndex == 5 && m.Fret == 1), "F on 6th string 1st fret is in Am");
Check(!plain.Any(m => m.StringIndex == 5 && m.Fret == 2), "F# is not in Am");
Check(Fretboard.Marks(amScale, 9, noChord).SequenceEqual(plain), "N.C. → no chord dots");
Check(Fretboard.Marks(amScale, 9, null, capo: 3).All(m => m.Fret >= 3), "no dots below the capo");

var withE7 = Fretboard.Marks(amScale, 9, E7);
Check(withE7.Where(m => m.PitchClass == 8).All(m => m.Kind == FretMarkKind.ChordToneOutsideScale)
    && withE7.Any(m => m.PitchClass == 8), "G# of E7 is outside Am");
Check(withE7.Where(m => m.PitchClass == 4).All(m => m.Kind == FretMarkKind.ChordRoot), "E is the chord root");
Check(withE7.Where(m => m.PitchClass == 2).All(m => m.Kind == FretMarkKind.ChordTone), "D (♭7 of E7) is a chord tone in the scale");
Check(withE7.Where(m => m.PitchClass == 9).All(m => m.Kind == FretMarkKind.ScaleRoot), "A stays the scale root");

// ---------- Chord forms ----------
// Every root × every chord type has a form, and every form is playable and sounds the chord.
var formProblems = new List<string>();
foreach (var type in catalog.Types)
{
    for (int root = 0; root < 12; root++)
    {
        var chord = new ChordResult(root, type, null, 1);
        var forms = ChordForms.For(chord);
        if (forms.Count == 0) { formProblems.Add($"{chord.Name}: no form"); continue; }
        var tones = Fretboard.ChordTones(chord);
        foreach (var form in forms)
        {
            var played = Enumerable.Range(0, 6).Where(s => form.FretOn(s) is not null)
                .Select(s => Fretboard.StandardTuning[s] + form.FretOn(s)!.Value).ToList();
            var fretted = form.Frets.Where(f => f > 0).Select(f => f!.Value).ToList();
            if (!played.All(p => tones.Contains(PitchClasses.Of(p)))) formProblems.Add($"{chord.Name} {form}: note outside the chord");
            if (!played.Any(p => PitchClasses.Of(p) == root)) formProblems.Add($"{chord.Name} {form}: no root");
            if (PitchClasses.Of(played.Min()) != root) formProblems.Add($"{chord.Name} {form}: bass is not the root");
            if (fretted.Count > 0 && fretted.Max() - fretted.Min() > 3) formProblems.Add($"{chord.Name} {form}: too wide");
            if (form.MaxFret > Fretboard.DefaultFretCount) formProblems.Add($"{chord.Name} {form}: above fret 15");
        }
    }
}
Check(formProblems.Count == 0, "every chord has playable forms: " + string.Join("; ", formProblems.Take(5)));
var Asus4 = Chord(9, "sus4");
Check(ChordForms.For(Asus4)[0].Text == "x02230" && ChordForms.For(C)[0].Text == "x32010", "open forms come first");
Check(ChordForms.For(F).Any(f => f.Text == "133211") && ChordForms.For(F)[0].Text == "133211", "F: barre form");
Check(ChordForms.For(Chord(11, "m"))[0].Text == "x24432", "Bm: A-form barre first");
Check(ChordForms.For(new ChordResult(0, catalog[""], 4, 1))[0].Text == "x32010", "slash bass ignored");
Check(ChordForms.For(noChord).Count == 0, "N.C. has no form");
Check(ChordForm.Parse("x-12-14-14-13-x").Text == "x-12-14-14-13-x" && ChordForm.Parse("x02230").FretOn(5) is null
    && ChordForm.Parse("x02230").FretOn(1) == 3, "form text and string order");

var asus4Marks = Fretboard.ChordFormMarks(ChordForm.Parse("x02230"), Asus4);
Check(asus4Marks.Single(m => m.StringIndex == 4 && m.Fret == 0).Kind == FretMarkKind.FormRoot, "Asus4: open 5th string is the root");
Check(!asus4Marks.Any(m => m.StringIndex == 5 && (m.Kind is FretMarkKind.FormRoot or FretMarkKind.FormTone || m.Fret == 0)), "Asus4: 6th string muted, nothing on its open string");
Check(asus4Marks.Single(m => m.StringIndex == 0 && m.Fret == 0).Kind == FretMarkKind.FormTone, "Asus4: open 1st string E is fretted in the form");
Check(asus4Marks.Single(m => m.StringIndex == 2 && m.Fret == 2).Kind == FretMarkKind.FormRoot
    && asus4Marks.Single(m => m.StringIndex == 5 && m.Fret == 5).Kind == FretMarkKind.OtherRoot, "Asus4: A on the 6th string 5th fret is another root");
Check(asus4Marks.Count(m => m.Kind is FretMarkKind.FormRoot or FretMarkKind.FormTone) == 5, "Asus4: five fretted notes");
var capoMarks = Fretboard.ChordFormMarks(ChordForm.Parse("x02230"), Chord(11, "sus4"), capo: 2);
Check(capoMarks.Where(m => m.Kind is FretMarkKind.FormRoot or FretMarkKind.FormTone).Select(m => (m.StringIndex, m.Fret)).Order()
    .SequenceEqual(new[] { (0, 2), (1, 5), (2, 4), (3, 4), (4, 2) }.Order()), "capo 2 moves the form up 2 frets");
Check(capoMarks.All(m => m.Fret >= 2), "no dots below the capo (chord form)");

Console.WriteLine($"{passed} checks passed");

static bool Throws(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
