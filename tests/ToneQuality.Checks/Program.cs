using System.Diagnostics;
using FXForge.AudioEffects;

int checks = 0;
const int Rate = 48000;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); checks++; }
ToneSettings raw = ToneSettings.Default with
{
    A = ToneSettings.Default.A with { Enabled = false },
    B = ToneSettings.Default.B with { Enabled = false },
    Amp = ToneSettings.Default.Amp with { Enabled = false }, OutputLevel = .65
};
ToneSettings sd1 = raw with { A = raw.A with { Enabled = true } };
ToneSettings rat = raw with { A = raw.B with { Enabled = true } };
float[] Tone(double frequency, double amplitude = .08, int length = Rate)
{
    var samples = new float[length];
    for (int i = 0; i < length; i++) samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / Rate));
    return samples;
}
float[] Render(ToneSettings settings, float[] input, int blockSize = 256)
{
    var engine = new ToneAudioEngine(); engine.SetSettings(settings);
    var output = (float[])input.Clone();
    var part = new float[blockSize];
    for (int at = 0; at < input.Length; at += blockSize)
    {
        int count = Math.Min(blockSize, input.Length - at);
        if (count == blockSize)
        {
            Array.Copy(input, at, part, 0, count); engine.Process(part, Rate); Array.Copy(part, 0, output, at, count);
        }
        else
        {
            var tail = input.AsSpan(at, count).ToArray(); engine.Process(tail, Rate); Array.Copy(tail, 0, output, at, count);
        }
    }
    return output;
}
double Rms(float[] x, int skip = Rate / 2) => Math.Sqrt(x.Skip(skip).Average(v => (double)v * v));
double Amplitude(float[] x, double hz)
{
    int start = x.Length - Rate / 2, count = Rate / 2;
    double real = 0, imaginary = 0;
    for (int i = 0; i < count; i++)
    {
        double phase = 2 * Math.PI * hz * i / Rate;
        real += x[start + i] * Math.Cos(phase); imaginary += x[start + i] * Math.Sin(phase);
    }
    return 2 * Math.Sqrt(real * real + imaginary * imaginary) / count;
}
Check(Math.Abs(DriveCurves.Sd1(.00001) - .00001) < 1e-10, "SD1 weak signal linearity");
Check(DriveCurves.Sd1(3) > -DriveCurves.Sd1(-3), "SD1 asymmetric saturation");
Check(DriveCurves.Rat(.1) == .1, "RAT linear below knee");
Check(DriveCurves.Rat(3) == -DriveCurves.Rat(-3), "RAT symmetric saturation");
Check(DriveCurves.Rat(3) <= DriveVoicing.Rat.ClipCeiling, "RAT ceiling");
Check(Math.Abs(DriveCurves.Rat(1.2) - DriveCurves.Rat(1)) < .000001, "RAT plateau");
bool smoothCurve = true;
for (double x = -.9; x < .9; x += .01)
    smoothCurve &= Math.Abs(DriveCurves.Sd1(x + .00001) - DriveCurves.Sd1(x)) <= .000011;
Check(smoothCurve, "SD1 continuous soft knee across operating range");
bool integralsAgree = true;
foreach (var (a, b) in new[] { (-.8, .9), (.1, .7), (.7, .70000000001), (-2.0, -.1), (0.35, .39) })
{
    var clipper = new AntialiasedRatClipper(); clipper.Process(a);
    double value = clipper.Process(b), reference = 0;
    for (int i = 0; i < 4096; i++) reference += DriveCurves.Rat(a + (b - a) * (i + .5) / 4096);
    integralsAgree &= Math.Abs(value - reference / 4096) < 1e-6;
}
Check(integralsAgree, "ADAA analytic average agrees with numerical integration");
var cleared = new AntialiasedRatClipper(); cleared.Process(3); cleared.Reset();
Check(cleared.Process(0) == 0, "ADAA reset removes prior sample");
var weakSd = Render(sd1, Tone(1000, .002)); var strongSd = Render(sd1, Tone(1000, .02));
var weakRat = Render(rat, Tone(1000, .002)); var strongRat = Render(rat, Tone(1000, .02));
double sdDynamics = Rms(strongSd) / Rms(weakSd), ratDynamics = Rms(strongRat) / Rms(weakRat);
Console.WriteLine($"Dynamics strong/weak RMS: SD1={sdDynamics:0.00}, RAT={ratDynamics:0.00}");
Check(sdDynamics > ratDynamics * 1.15, "SD1 preserves more picking dynamics than heavy RAT preset");
var sdLow = Render(sd1, Tone(82)); var ratLow = Render(rat, Tone(82));
var sdMid = Render(sd1, Tone(1000)); var ratMid = Render(rat, Tone(1000));
double sdBody = Rms(sdLow) / Rms(sdMid), ratBody = Rms(ratLow) / Rms(ratMid);
Console.WriteLine($"Low/mid RMS: SD1={sdBody:0.00}, RAT={ratBody:0.00}");
Check(ratBody > .5 && sdBody > .5, "both new presets retain low-string body");
var sdTone = Render(sd1, Tone(500)); var ratTone = Render(rat, Tone(500));
double sdOdd = Amplitude(sdTone, 1500) / Amplitude(sdTone, 500);
double ratOdd = Amplitude(ratTone, 1500) / Amplitude(ratTone, 500);
Console.WriteLine($"Third/fundamental: SD1={sdOdd:0.000}, RAT={ratOdd:0.000}");
Check(HighHarmonics(ratTone) > HighHarmonics(sdTone) * 1.3, "new RAT adds upper harmonics beyond the denser SD1");
var previousRat = new float[Rate];
byte[] previousBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PreviousRat500.f32"));
Buffer.BlockCopy(previousBytes, 0, previousRat, 0, previousBytes.Length);
double previousRms = Rms(previousRat), mappedRms = Rms(sdTone);
double mappingError = Math.Sqrt(sdTone.Skip(Rate / 2).Select((v, i) =>
    Math.Pow(v - previousRat[Rate / 2 + i], 2)).Average()) / previousRms;
Console.WriteLine($"New SD1 vs previous RAT: RMS ratio={mappedRms / previousRms:0.000}, normalized error={mappingError:0.000}");
Check(mappedRms / previousRms > .8 && mappedRms / previousRms < 1.2 && mappingError < .2,
    "new SD1 approximates previous RAT density and level");
double HighHarmonics(float[] x) => Math.Sqrt(new[] { 2500, 3500, 4500 }.Sum(h => Math.Pow(Amplitude(x, h), 2))) / Amplitude(x, 500);
Console.WriteLine($"High harmonic ratio: previous RAT={HighHarmonics(previousRat):0.000}, new RAT={HighHarmonics(ratTone):0.000}");
Check(HighHarmonics(ratTone) > HighHarmonics(previousRat) * 1.3, "new RAT has more upper harmonic density than previous RAT");
var sdAmp = Render(sd1 with { Amp = ToneSettings.Default.Amp }, Tone(500));
var ratAmp = Render(rat with { Amp = ToneSettings.Default.Amp }, Tone(500));
Check(HighHarmonics(ratAmp) > HighHarmonics(sdAmp) * 1.3,
    "clean comparison AMP preserves SD1/RAT harmonic distinction");

// 非線形処理のない往復FIRで、音量と遅延・ブロック連続性を独立確認。
var sampler = new Oversampler8x();
double last = 0;
for (int i = 0; i < 1000; i++) { sampler.Up(.25); last = sampler.Down(); }
Check(Math.Abs(last - .25) < 1e-10, "FIR DC unity gain");
sampler.Reset(); var impulse = new double[200];
for (int i = 0; i < impulse.Length; i++) { sampler.Up(i == 0 ? 1 : 0); impulse[i] = sampler.Down(); }
int delay = Array.IndexOf(impulse, impulse.Max());
Console.WriteLine($"FIR round-trip impulse peak: {delay} samples / {delay * 1000.0 / Rate:0.00} ms at 48kHz");
Check(delay > 20 && delay < 100, "FIR low millisecond delay");
Check(Math.Abs(impulse.Sum() - 1) < 1e-5, "FIR impulse area");

var alias = Render(rat with { A = rat.A with { Tone = 0 } }, Tone(9000, .12));
double fundamental = Amplitude(alias, 9000);
double folded = Math.Sqrt(Math.Pow(Amplitude(alias, 3000), 2) + Math.Pow(Amplitude(alias, 15000), 2) + Math.Pow(Amplitude(alias, 21000), 2));
double aliasDb = 20 * Math.Log10(folded / fundamental);
Console.WriteLine($"9kHz high-drive folded components / fundamental: {aliasDb:0.0} dB");
Check(aliasDb < -35, "folded harmonics suppressed in high-drive test");

var pulse = new float[Rate];
var note = Tone(82, .12, Rate / 10); Array.Copy(note, pulse, note.Length);
var decay = Render(rat, pulse);
Check(decay.Skip(Rate / 2).Max(v => Math.Abs(v)) < 1e-5, "RAT body does not create persistent boom after muting");

// 使っていない時間の状態を更新し、再度ONで過去の音声を再生しない。
var switcher = new ToneAudioEngine(); switcher.SetSettings(rat);
var active = Tone(220, .1, 4096); switcher.Process(active, Rate);
switcher.SetSettings(raw);
for (int i = 0; i < 120; i++) switcher.Process(new float[256], Rate);
switcher.SetSettings(rat); var silence = new float[2048]; switcher.Process(silence, Rate);
Check(silence.Max(v => Math.Abs(v)) < 1e-7, "no stale audio when re-enabling effects");

if (args.Length > 0)
{
    string folder = args[0]; Directory.CreateDirectory(folder);
    void FloatFile(string name, float[] samples) => File.WriteAllBytes(Path.Combine(folder, name), System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()).ToArray());
    FloatFile("sd_500.f32", sdTone); FloatFile("rat_500.f32", ratTone);
    FloatFile("sd_low.f32", sdLow); FloatFile("rat_low.f32", ratLow);
    FloatFile("sd_mid.f32", sdMid); FloatFile("rat_mid.f32", ratMid); FloatFile("alias_9k.f32", alias);
    FloatFile("sd_curve_input.f32", Tone(500));
    var guitar = new float[Rate * 7];
    var random = new Random(51);
    foreach (double onset in new[] { .15, .45, .75, 1.05, 1.45, 1.75, 2.05, 2.35, 2.75, 3.0, 3.25, 3.5, 3.9, 4.15, 4.4, 4.65, 5.1, 5.65 })
    {
        bool muted = onset < 5;
        for (int i = 0; i < (muted ? Rate / 4 : Rate); i++)
        {
            int at = (int)(onset * Rate) + i; if (at >= guitar.Length) break;
            double t = i / (double)Rate, env = (1 - Math.Exp(-t / .001)) * Math.Exp(-t / (muted ? .055 : .28));
            double value = 0;
            foreach (double frequency in muted ? new[] { 82.4069, 123.4708 } : new[] { 164.8138, 246.9417, 329.6276 })
                for (int harmonic = 1; harmonic <= 9; harmonic++)
                    value += Math.Sin(2 * Math.PI * frequency * harmonic * t + .17 * harmonic) /
                        Math.Pow(harmonic, 1.4) * Math.Exp(-t * harmonic * 3);
            double attack = (random.NextDouble() * 2 - 1) * .12 * Math.Exp(-t / .003);
            guitar[at] += (float)(.075 * env * (value + attack));
        }
    }
    FloatFile("demo_input.f32", guitar);
    FloatFile("demo_sd.f32", Render(sd1 with { Amp = ToneSettings.Default.Amp }, guitar));
    FloatFile("demo_rat.f32", Render(rat with { Amp = ToneSettings.Default.Amp }, guitar));
    // 過去版と同一入力でスペクトルを比較するための信号。
    FloatFile("alias_input.f32", Tone(9000, .12));
}
Console.WriteLine($"PASS: {checks} tone quality checks");
