using ChordDetection.Core.Chords;
using ChordDetection.Core.Music;

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

Console.WriteLine($"{passed} checks passed");

static bool Throws(Action action) { try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; } }
