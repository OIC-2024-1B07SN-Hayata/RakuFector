using System.Text.Json;
using CommonAudio;
using FXForge;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
}
float[] Read(PcMonitorWaveProvider source, int frames)
{
    var bytes = new byte[frames * 8];
    if (source.Read(bytes, 0, bytes.Length) != bytes.Length) throw new Exception("Read stopped");
    var values = new float[frames * 2];
    Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
    return values;
}
var p = new PcMonitorWaveProvider(48000);
Check(p.WaveFormat.SampleRate == 48000 && p.WaveFormat.Channels == 2 && p.WaveFormat.BitsPerSample == 32, "stereo float");
Check(Read(p, 17).All(x => x == 0), "silence before input does not stop playback");
var shared = new float[] { -.3f, .2f, .5f };
p.SetInput(shared); Array.Fill(shared, .9f);
var first = Read(p, 2);
Check(first.SequenceEqual(new float[] { -.3f, -.3f, .2f, .2f }), "snapshot and smaller read");
p.SetInput(new float[] { .7f, -.8f });
var second = Read(p, 5);
Check(second.SequenceEqual(new float[] { .5f, .5f, .7f, .7f, -.8f, -.8f, 0, 0, 0, 0 }), "remaining input joins next block, tail is silence");
Check(Read(p, 9).All(x => x == 0), "stale audio is not repeated");
p.SetInput(new float[] { float.NaN, float.PositiveInfinity, 3, -4 });
Check(Read(p, 4).SequenceEqual(new float[] { 0, 0, 0, 0, 1, 1, -1, -1 }), "sanitize and clamp");
p.SetInput(new float[] { .3f }); p.Clear();
Check(Read(p, 1).All(x => x == 0), "clear removes pending audio");
var offset = Enumerable.Repeat((byte)19, 24).ToArray();
p.SetInput(new float[] { .4f }); p.Read(offset, 8, 8);
Check(offset.Take(8).All(x => x == 19) && offset.Skip(16).All(x => x == 19), "writes only requested span");
Check(BitConverter.ToSingle(offset, 8) == .4f && BitConverter.ToSingle(offset, 12) == .4f, "offset stereo data");

// 出力クロックが遅れても指定した上限を超えず、新しい入力を残す。
var small = new PcMonitorWaveProvider(1000, 100);
small.SetInput(Enumerable.Range(0, 80).Select(i => i / 200f).ToArray());
small.SetInput(Enumerable.Range(80, 80).Select(i => i / 200f).ToArray());
Check(small.BufferedFrames == 100 && small.DroppedFrames == 60, "queue bounded during clock drift");
var kept = Read(small, 100);
Check(kept[0] == .3f && kept[^1] == .795f, "oldest samples discarded, latest retained");
small.SetInput(Enumerable.Range(0, 250).Select(i => i / 250f).ToArray());
Check(small.BufferedFrames == 100 && small.DroppedFrames == 210, "oversized input retains bounded tail");
Check(Read(small, 1)[0] == .6f, "oversized input tail starts at expected sample");
foreach (int rate in new[] { 8000, 44100, 48000, 96000, 192000 })
{
    var source = new PcMonitorWaveProvider(rate);
    foreach (int frames in new[] { 32, 127, 256, 1024 })
    {
        source.SetInput(Enumerable.Repeat(.25f, frames).ToArray());
        var output = Read(source, frames + 13);
        Check(output.Count(x => x == .25f) == Math.Min(frames, source.MaximumBufferedFrames) * 2 &&
              output.All(x => x == .25f || x == 0), "different rates and callback sizes");
    }
}

// 別スレッドで生産と消費を行い、破損・左右のずれ・順序の逆転がないことを確認。
var concurrent = new PcMonitorWaveProvider(48000);
var producer = Task.Run(() =>
{
    var block = new float[64];
    for (int i = 0; i < 1500; i++)
    {
        Array.Fill(block, (i + 1) / 2000f);
        concurrent.SetInput(block);
    }
});
var consumer = Task.Run(() =>
{
    float previous = 0;
    var bytes = new byte[137 * 8];
    for (int i = 0; i < 1500; i++)
    {
        concurrent.Read(bytes, 0, bytes.Length);
        for (int at = 0; at < bytes.Length; at += 8)
        {
            float left = BitConverter.ToSingle(bytes, at), right = BitConverter.ToSingle(bytes, at + 4);
            if (!float.IsFinite(left) || left != right || (left != 0 && left < previous)) return false;
            if (left != 0) previous = left;
        }
    }
    return true;
});
Task.WaitAll(producer, consumer);
Check(consumer.Result && concurrent.BufferedFrames <= concurrent.MaximumBufferedFrames, "concurrent producer/consumer");
var old = JsonSerializer.Deserialize<AudioPreferences>("{\"Driver\":\"ASIO\",\"Input\":1,\"MonitorOutput\":true}");
Check(old is { PcOutput: true, PcDeviceId: null, Input: 1 }, "old settings default to PC output");
var saved = new AudioPreferences("ASIO", 1, 48000, true, 0, true, "speaker-id", "PC Speaker");
Check(JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(saved)) == saved, "stable device ID saved");
var asio = saved with { PcOutput = false };
Check(JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(asio))?.PcOutput == false, "explicit ASIO route retained");
var noOutput = saved with { MonitorOutput = false };
Check(JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(noOutput))?.MonitorOutput == false, "output off retained");
var hot = new PcMonitorWaveProvider(48000); var data = new float[256]; var bytesHot = new byte[256 * 8];
for (int i = 0; i < 20; i++) { hot.SetInput(data); hot.Read(bytesHot, 0, bytesHot.Length); }
long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++) { hot.SetInput(data); hot.Read(bytesHot, 0, bytesHot.Length); }
Check(GC.GetAllocatedBytesForCurrentThread() == before, "stable queue loop has no managed allocation");
Check(p.MaximumBufferedFrames == 960, "default queue reduced to 20ms at 48kHz");
Check(AudioLatency.QueueCapacityFrames(48000, 128, 10) == 960, "small ASIO block keeps 20ms queue cap");
Check(AudioLatency.QueueCapacityFrames(48000, 1024, 10) >= 1024 + 480, "large ASIO block retained without truncation");
Check(AudioLatency.QueueCapacityFrames(48000, 256, 20) >= 256 + 960, "stable setting expands queue for output period");
Check(old?.PcLatencyMs == 10, "old settings migrate to low PC latency request");
var stable = saved with { PcLatencyMs = 20 };
Check(JsonSerializer.Deserialize<AudioPreferences>(JsonSerializer.Serialize(stable))?.PcLatencyMs == 20, "latency setting saved");
var simulated = new PcMonitorWaveProvider(48000, AudioLatency.QueueCapacityFrames(48000, 128, 10));
var packet = Enumerable.Repeat(.2f, 128).ToArray();
for (int i = 0; i < 100; i++) simulated.SetInput(packet);
Check(simulated.BufferedFrames == 960 && simulated.DroppedFrames == 12800 - 960,
    "stalled PC consumer retains at most 20ms of newest audio");
Console.WriteLine($"PASS: {checks} PC output checks");
