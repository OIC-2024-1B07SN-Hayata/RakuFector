using System.Diagnostics;
using System.Runtime.InteropServices;
using DrumEditor.Domain;

namespace DrumEditor.Audio;

public sealed class DrumAudio : IDisposable
{
    const uint CallbackEvent = 0x50000, InQueue = 16;
    public const int BufferCount = 4, BufferMilliseconds = 20;
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormat { public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct Header { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

    readonly object gate = new();
    readonly AutoResetEvent bufferReady = new(false);
    readonly DrumRenderSource source;
    readonly Thread worker;
    volatile bool quit, playing;
    bool startRequested, stopRequested;
    Instrument? preview;
    IntPtr device;
    float volume = DeviceOutputStage.DefaultVolume;
    int disposed, underruns, buffersWritten;
    long longestFillTicks;

    public bool Connected => Volatile.Read(ref device) != IntPtr.Zero && !quit;
    public int SampleRate => source.Rate;
    public bool Playing => playing;
    int loopStart,loopEnd;
    public void SetLoopRange(int startTick,int endTick){loopStart=startTick;loopEnd=endTick;}
    public double Tick => loopStart + Volatile.Read(ref source.PositionFrames) * 960d * Bpm / (60 * source.Rate);
    public int Bpm = 40;
    public string Status { get; private set; } = "未接続";
    public int UnderrunCount => Volatile.Read(ref underruns);
    public int BuffersWritten => Volatile.Read(ref buffersWritten);
    public double LongestFillMilliseconds => Volatile.Read(ref longestFillTicks) * 1000d / Stopwatch.Frequency;
    public float Volume
    {
        get => Volatile.Read(ref volume);
        set
        {
            if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref volume, value);
        }
    }

    string? publishedKey;long pendingRevision;
    public bool WaitingForLoop => Playing && Volatile.Read(ref source.AppliedRevision)<pendingRevision;
    public void Publish(Pattern p)
    {
        string key=System.Text.Json.JsonSerializer.Serialize(new{p.Bpm,p.BeatsPerBar,p.Bars,p.SwingPercent,p.Notes,loopStart,loopEnd});
        if(key!=publishedKey){pendingRevision=source.Publish(p,loopStart,loopEnd>0?loopEnd:null);publishedKey=key;}else source.SetTrackVolumes(p); Bpm = p.Bpm;
        source.EnabledMask = p.Tracks.Where(t => t.Enabled).Sum(t => 1 << (int)t.Instrument);
        source.Metronome = p.MetronomeEnabled;
    }
    public void SetTrackVolumes(Pattern p) => source.SetTrackVolumes(p);
    public void Solo(int mask) => source.SoloMask = mask;

    public DrumAudio(SampleKit kit)
    {
        source = new DrumRenderSource(kit);
        var format = new WaveFormat { Tag = 1, Channels = 2, Rate = (uint)kit.SampleRate, BytesPerSecond = (uint)(kit.SampleRate * 4), BlockAlign = 4, Bits = 16 };
        try
        {
            int error = waveOutOpen(out device, uint.MaxValue, ref format, bufferReady.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            if (error != 0) throw new InvalidOperationException($"音声デバイスを開けません（{error}）。");
            if (waveOutPause(device) != 0) throw new IOException("音声出力の初期バッファ準備に失敗しました。");
            Status = "waveOut · バッファ80ms · 遅延不明";
            worker = new Thread(Run) { IsBackground = true, Name = "Drum PCM", Priority = ThreadPriority.AboveNormal };
            worker.Start();
        }
        catch
        {
            if (device != IntPtr.Zero) { waveOutClose(device); device = IntPtr.Zero; }
            bufferReady.Dispose(); throw;
        }
    }

    public void Play() { lock (gate) startRequested = true; }
    public void Stop() { lock (gate) stopRequested = true; }
    public void Preview(Instrument instrument) { lock (gate) preview = instrument; }

    void Run()
    {
        int frames = source.Rate * BufferMilliseconds / 1000;
        uint size = (uint)Marshal.SizeOf<Header>();
        int flagsOffset = (int)Marshal.OffsetOf<Header>(nameof(Header.Flags));
        var headers = new IntPtr[BufferCount]; var buffers = new IntPtr[BufferCount]; var prepared = new bool[BufferCount];
        var output = new float[frames * 2]; var pcm = new short[frames * 2]; var stage = new DeviceOutputStage(source.Rate);
        long frame = 0;
        bool primed = false;
        uint Flags(int index) => (uint)Marshal.ReadInt32(headers[index], flagsOffset);
        try
        {
            for (int i = 0; i < BufferCount; i++)
            {
                buffers[i] = Marshal.AllocHGlobal(pcm.Length * 2); headers[i] = Marshal.AllocHGlobal((int)size);
                Marshal.StructureToPtr(new Header { Data = buffers[i], Length = (uint)(pcm.Length * 2) }, headers[i], false);
                if (waveOutPrepareHeader(device, headers[i], size) != 0) throw new IOException("音声バッファ準備に失敗しました。");
                prepared[i] = true;
            }
            int index = 0;
            while (!quit)
            {
                // Never rewrite a buffer still owned by the driver. A callback wakes the worker.
                if ((Flags(index) & InQueue) != 0) { bufferReady.WaitOne(100); continue; }
                if (primed)
                {
                    bool anyQueued = false;
                    for (int i = 0; i < BufferCount; i++) anyQueued |= (Flags(i) & InQueue) != 0;
                    if (!anyQueued) Interlocked.Increment(ref underruns);
                }
                long started = Stopwatch.GetTimestamp();
                lock (gate)
                {
                    if (stopRequested) { source.FadeStop(); playing = false; stopRequested = false; startRequested = false; }
                    if (startRequested) { source.Start(frame); playing = true; startRequested = false; }
                    if (preview is { } instrument) { source.Preview(instrument); preview = null; }
                }
                Array.Clear(output); source.RenderAdd(output, frames, frame, playing);
                stage.Process(output, frames, Volume);
                for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(Math.Clamp(output[i], -1, 1) * 32767);
                Marshal.Copy(pcm, 0, buffers[index], pcm.Length);
                if (waveOutWrite(device, headers[index], size) != 0) throw new IOException("音声デバイスへの出力に失敗しました。");
                Interlocked.Increment(ref buffersWritten);
                Volatile.Write(ref longestFillTicks, Math.Max(longestFillTicks, Stopwatch.GetTimestamp() - started));
                frame += frames; index = (index + 1) % BufferCount;
                // Fill the entire queue before starting, protecting playback from first-use JIT.
                if (!primed && index == 0)
                {
                    if (waveOutRestart(device) != 0) throw new IOException("音声出力を開始できません。");
                    primed = true;
                }
            }
        }
        catch (Exception ex) { Status = "未接続: " + ex.Message; playing = false; }
        finally
        {
            waveOutReset(device);
            for (int i = 0; i < BufferCount; i++)
            {
                if (headers[i] != IntPtr.Zero)
                {
                    if (prepared[i]) waveOutUnprepareHeader(device, headers[i], size);
                    Marshal.FreeHGlobal(headers[i]);
                }
                if (buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(buffers[i]);
            }
            waveOutClose(device); Volatile.Write(ref device, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        quit = true; bufferReady.Set(); worker.Join(); bufferReady.Dispose();
    }
}
