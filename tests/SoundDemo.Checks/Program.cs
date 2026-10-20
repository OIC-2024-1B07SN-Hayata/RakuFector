using System.Diagnostics;
using System.Text.Json;
using CommonAudio;
using FXForge;
using FXForge.AudioEffects;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); checks++; }
float[] Tone(int rate, double frequency, double amplitude = .08, int seconds = 1)
{
    float[] data = new float[rate * seconds];
    for (int i = 0; i < data.Length; i++) data[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate));
    return data;
}
float[] Render(ToneSettings settings, float[] input, int rate = 48000, int block = 256)
{
    var engine = new ToneAudioEngine(); engine.SetSettings(settings);
    var output = new float[input.Length];
    for (int start = 0; start < input.Length; start += block)
    {
        float[] part = input.AsSpan(start, Math.Min(block, input.Length - start)).ToArray();
        engine.Process(part, rate); Array.Copy(part, 0, output, start, part.Length);
    }
    return output;
}
double Rms(float[] data) => Math.Sqrt(data.Skip(data.Length / 4).Average(x => (double)x * x));
double Difference(float[] a, float[] b) => Math.Sqrt(a.Skip(a.Length / 4).Select((x, i) =>
    Math.Pow(x - b[i + a.Length / 4], 2)).Average());
bool Safe(float[] data) => data.All(x => float.IsFinite(x) && Math.Abs(x) <= .98001f);
ToneSettings raw = ToneSettings.Default with
{
    A = ToneSettings.Default.A with { Enabled = false },
    B = ToneSettings.Default.B with { Enabled = false },
    Amp = ToneSettings.Default.Amp with { Enabled = false }, OutputLevel = .65
};
ToneSettings sd1 = raw with { A = raw.A with { Enabled = true } };
ToneSettings rat = raw with { A = raw.B with { Enabled = true } };
ToneSettings amp = raw with { Amp = raw.Amp with { Enabled = true, Gain = .20 } };
float[] input = Tone(48000, 440);
float[] dry = Render(raw, input);
Check(dry.Zip(input).All(p => Math.Abs(p.First - p.Second * .65) < 1e-7), "all bypass preserves waveform at master level");
Check(Rms(dry) > .02, "raw audible signal");
float[] sd = Render(sd1, input), rt = Render(rat, input), am = Render(amp, input);
Check(Safe(sd) && Rms(sd) > .001, "SD1 finite audible output");
Check(Safe(rt) && Rms(rt) > .001, "RAT finite audible output");
Check(Safe(am) && Rms(am) > .001, "AMP finite audible output");
Check(Difference(sd, dry) > .005, "SD1 changes waveform");
Check(Difference(rt, dry) > .005, "RAT changes waveform");
Check(Difference(sd, rt) > .005, "SD1 differs from RAT");

foreach (var settings in new[] { raw, sd1, rat, amp, ToneSettings.Default })
{
    Check(Render(settings, new float[48000]).All(x => x == 0), "zero input stays zero");
    Check(Safe(Render(settings, Enumerable.Repeat(10f, 48000).ToArray())), "large input bounded");
}
Check(Rms(Render(sd1 with { A = sd1.A with { Drive = 1 } }, input)) >
      Rms(Render(sd1 with { A = sd1.A with { Drive = 0 } }, input)) * 1.5, "SD1 Drive affects gain");
Check(Rms(Render(rat with { A = rat.A with { Drive = 1 } }, input)) >
      Rms(Render(rat with { A = rat.A with { Drive = 0 } }, input)) * 1.5, "RAT Distortion affects gain");
Check(Rms(Render(amp with { Amp = amp.Amp with { Gain = 1 } }, input)) >
      Rms(Render(amp with { Amp = amp.Amp with { Gain = 0 } }, input)) * 1.5, "AMP Gain affects signal");

float[] high = Tone(48000, 4000, .04);
Check(Rms(Render(sd1 with { A = sd1.A with { Tone = 1 } }, high)) >
      Rms(Render(sd1 with { A = sd1.A with { Tone = 0 } }, high)) * 2, "SD1 Tone opens high end");
Check(Rms(Render(rat with { A = rat.A with { Tone = 0 } }, high)) >
      Rms(Render(rat with { A = rat.A with { Tone = 1 } }, high)) * 2, "RAT Filter cuts high end clockwise");
foreach (var settings in new[] { sd1, rat })
    Check(Rms(Render(settings with { A = settings.A with { Level = 0 } }, input)) < 1e-6, "drive level zero mutes");
Check(Rms(Render(amp with { Amp = amp.Amp with { Level = 0 } }, input)) < 1e-6, "AMP Level zero mutes");
Check(Render(ToneSettings.Default with { OutputLevel = 0 }, input).All(x => x == 0), "master zero mutes");

float[] low = Tone(48000, 82.4069, .02), mid = Tone(48000, 800, .02);
Check(Rms(Render(amp with { Amp = amp.Amp with { Bass = 1 } }, low)) >
      Rms(Render(amp with { Amp = amp.Amp with { Bass = 0 } }, low)) * 2, "AMP Bass EQ");
Check(Rms(Render(amp with { Amp = amp.Amp with { Middle = 1 } }, mid)) >
      Rms(Render(amp with { Amp = amp.Amp with { Middle = 0 } }, mid)) * 2, "AMP Middle EQ");
Check(Rms(Render(amp with { Amp = amp.Amp with { Treble = 1 } }, high)) >
      Rms(Render(amp with { Amp = amp.Amp with { Treble = 0 } }, high)) * 2, "AMP Treble EQ");
ToneSettings both = sd1 with { B = rat.A };
Check(Difference(Render(both, input), Render(both with { ReverseDrives = true }, input)) > .001,
    "drive chain order affects output");
Check(Math.Abs(sd.Skip(24000).Average(x => (double)x)) < .001, "SD1 DC removed");

foreach (int rate in new[] { 8000, 44100, 48000, 96000, 192000 })
    Check(Safe(Render(both with { Amp = amp.Amp }, Tone(rate, 110), rate)), "sample rate supported");
foreach (int block in new[] { 32, 64, 128, 256, 1024 })
    Check(Difference(Render(sd1, input, block: block), sd) < 1e-7, "buffer size independence");
var changing = new ToneAudioEngine();
for (int pass = 0; pass < 40; pass++)
{
    changing.SetSettings(pass % 2 == 0 ? both : amp);
    var part = Tone(pass % 3 == 0 ? 96000 : 48000, 220).AsSpan(0, 256).ToArray();
    changing.Process(part, pass % 3 == 0 ? 96000 : 48000);
    Check(Safe(part), "hot settings/rate transition");
}
float[] invalid = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, .1f };
changing.Process(invalid, 48000);
Check(Safe(invalid), "non-finite input handled");

var provider = new AsioMonitorWaveProvider(48000); provider.Prepare(256);
float[] original = Tone(48000, 220).AsSpan(0, 256).ToArray();
float[] originalCopy = (float[])original.Clone(), processed = (float[])original.Clone();
var live = new ToneAudioEngine(); live.SetSettings(sd1); live.Process(processed, 48000);
provider.SetInput(processed); Array.Fill(processed, 0f);
byte[] bytes = new byte[256 * 8]; provider.Read(bytes, 0, bytes.Length);
float[] stereo = new float[512]; Buffer.BlockCopy(bytes, 0, stereo, 0, bytes.Length);
Check(original.SequenceEqual(originalCopy), "raw input unaffected by output processing");
Check(Enumerable.Range(0, 256).All(i => stereo[i*2] == stereo[i*2+1]), "processed audio reaches both output channels");
Check(stereo.Any(x => Math.Abs(x) > .0001), "output copy retained after reused source");
Check(provider.ReadCount == 1, "output counter increments on valid read");
provider.Read(bytes, 0, bytes.Length);
Check(bytes.All(x => x == 0), "no repeated stale buffer");
provider.SetInput(original); provider.Read(new byte[16], 0, 16);
Check(provider.LastError is not null, "wrong output size is visible");

var old = JsonSerializer.Deserialize<AudioPreferences>("{\"Driver\":\"ASIO\",\"Input\":1,\"Rate\":48000}");
Check(old is { MonitorOutput: true, Output: 0, Input: 1 }, "input-only settings migrate with output default");
var prefs = new AudioPreferences("ASIO", 1, 48000, true, 0);
Check(JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(prefs)) == prefs, "settings round trip");

// 安定した設定での音声処理がヒープへ割り当てないことと処理時間を確認。
var bench = new ToneAudioEngine(); bench.SetSettings(both with { Amp = amp.Amp });
var buffer = new float[256];
for (int i = 0; i < 30; i++) bench.Process(buffer, 48000);
long before = GC.GetAllocatedBytesForCurrentThread();
long start = Stopwatch.GetTimestamp();
for (int i = 0; i < 1000; i++) { Array.Fill(buffer, .05f); bench.Process(buffer, 48000); }
double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
long allocations = GC.GetAllocatedBytesForCurrentThread() - before;
Check(allocations == 0, "stable DSP loop has no managed allocations");
Console.WriteLine($"PASS: {checks} checks; DSP 1000 x 256 frames: {elapsed:0} ms; managed allocation: {allocations} bytes");
