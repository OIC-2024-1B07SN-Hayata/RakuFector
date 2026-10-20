using System;
using System.Text.Json;
using CommonAudio;
using FXForge;
using NAudio.Wave;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
}
float[] Read(AsioMonitorWaveProvider provider, int frames)
{
    byte[] bytes = new byte[frames * 2 * sizeof(float)];
    Check(provider.Read(bytes, 0, bytes.Length) == bytes.Length, "continuous output size");
    float[] samples = new float[frames * 2];
    Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
    return samples;
}
bool AllZero(float[] samples) => Array.TrueForAll(samples, s => s == 0);

var provider = new AsioMonitorWaveProvider(48000);
provider.Prepare(3);
Check(provider.WaveFormat.SampleRate == 48000, "rate");
Check(provider.WaveFormat.Channels == 2, "stereo");
Check(provider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat, "float format");
Check(AllZero(Read(provider, 3)), "silence before input");

float[] input = { -0.25f, 0, 0.75f };
provider.SetInput(input);
Check(input[0] == -0.25f && input[2] == 0.75f, "shared input unchanged");
input[0] = 0.9f; // 共通配列が再利用されても、コピーした出力は変わらない。
float[] output = Read(provider, 3);
Check(output[0] == -0.25f && output[1] == -0.25f, "left and right negative sample");
Check(output[2] == 0 && output[3] == 0, "left and right zero sample");
Check(output[4] == 0.75f && output[5] == 0.75f, "left and right positive sample");
Check(AllZero(Read(provider, 3)), "old input not replayed");

provider.SetInput(new float[] { 0.2f, 0.3f, 0.4f });
provider.Clear();
Check(AllZero(Read(provider, 3)), "clear silences output");
provider.SetInput(new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity });
Check(AllZero(Read(provider, 3)), "non-finite input silenced");

bool threw = false;
try { provider.SetInput(new float[2]); }
catch (InvalidOperationException) { threw = true; }
Check(threw, "changed frame count rejected");
provider.Prepare(2);
provider.SetInput(new float[] { 0.1f, -0.1f });
output = Read(provider, 2);
Check(output[0] == 0.1f && output[1] == 0.1f &&
      output[2] == -0.1f && output[3] == -0.1f, "new buffer size");

provider.SetInput(new float[] { 0.3f, 0.4f });
byte[] offsetBytes = new byte[24];
Array.Fill(offsetBytes, (byte)99);
Check(provider.Read(offsetBytes, 4, 16) == 16, "read at offset");
Check(offsetBytes[0] == 99 && offsetBytes[23] == 99, "outside output unchanged");
Check(BitConverter.ToSingle(offsetBytes, 4) == 0.3f, "offset contains signal");

var oldPreferences = JsonSerializer.Deserialize<AudioPreferences>(
    "{\"Driver\":\"Test ASIO\",\"Input\":1,\"Rate\":48000}");
Check(oldPreferences is not null && oldPreferences.MonitorOutput && oldPreferences.Input == 1,
    "old input-only preferences migrate with output enabled");
var preferences = new AudioPreferences("Test ASIO", 1, 48000, true);
var roundTrip = JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(preferences));
Check(roundTrip == preferences, "output setting round trip");
Console.WriteLine($"PASS: {checks} checks");
