using System.Text.Json;
using DrumEditor.Domain;
using DrumEditor.Files;
using DrumEditor.Audio;

int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
void Reject(Action action, string label, string? message = null)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { Check(message == null || ex.Message.Contains(message), label); return; }
    throw new Exception(label + ": rejected input was accepted");
}
SampleKit ImpulseKit(int rate = 44100, int duration = 2000)
{
    var samples = new DrumSample[13];
    for (int i = 0; i < 13; i++) { var pcm = new float[duration * 2]; pcm[0] = pcm[1] = 1; samples[i] = new(pcm, 1); }
    return new("test-impulses", rate, samples);
}
SampleKit ConstantKit(int frames)
{
    var samples = new DrumSample[13];
    for (int i = 0; i < 13; i++) samples[i] = new(Enumerable.Repeat(.25f, frames * 2).ToArray(), 1);
    return new("test-decay", 44100, samples);
}
float[] Render(Pattern pattern, int frames, SampleKit kit, int mask = InstrumentCatalog.EnabledMask, int solo = 0)
{
    var source = new DrumRenderSource(kit) { EnabledMask = mask, SoloMask = solo };
    source.Publish(pattern); source.Start(0); var buffer = new float[frames * 2]; source.RenderAdd(buffer, frames, 0, true); return buffer;
}
var editor = new Editor();
Check(editor.Pattern.SchemaVersion == 3 && editor.Pattern.Tracks.Count == 13 && editor.Pattern.Bpm == 40 && editor.Pattern.Notes.Count == 0, "初期値・13音色");
Check(InstrumentCatalog.All.Select(d => d.Name).SequenceEqual(new[] { "Kick", "Snare", "HiHat", "OpenHH", "Clap", "Perc", "LowTom", "LowMiddleTom", "HighMiddleTom", "HighTom", "FloorTom", "Crash", "Ride" }), "写真の英語名とカード順");
Check((int)Instrument.Perc == 5 && (int)Instrument.HighMiddleTom == 8 && InstrumentCatalog.EnabledMask == 8191, "旧IDと13bitマスク");
editor.Change(p => p.Notes.Add(new(Guid.NewGuid(), Instrument.Ride, 0, 100))); editor.Undo(); Check(editor.Pattern.Notes.Count == 0, "追加音色Undo"); editor.Redo(); Check(editor.Pattern.Notes[0].Instrument == Instrument.Ride, "追加音色Redo");
var p = editor.Pattern.Copy(); p.Notes.Add(p.Notes[0]); Reject(()=>p.Validate(), "重複拒否");
Check(Editor.RemapTick(4800, 4, 3) == 3840, "拍子変更で小節内位置保持");
string beforeFailedEdit=editor.Pattern.Fingerprint();try{editor.Change(pattern=>{pattern.Bpm=999;throw new InvalidOperationException("test cancellation");});}catch(InvalidOperationException){}Check(editor.Pattern.Fingerprint()==beforeFailedEdit,"途中で失敗した編集を一括取り消し");
string workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
string testRoot = Path.GetFullPath(Path.GetTempPath());
string dir = Path.Combine(testRoot, "DrumEditorTests-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
try
{
    string json = Path.Combine(dir, "test.drum.json"), midi = Path.Combine(dir, "test.mid");
    p = new Pattern { Bpm = 120, Bars = 3 };
    foreach (var d in InstrumentCatalog.All) p.Notes.Add(new(Guid.NewGuid(), d.Id, (int)d.Id * 240, 80 + (int)d.Id));
    p.Tracks[8] = p.Tracks[8] with { Enabled = false }; p.SelectedInstrument = Instrument.Ride;
    PatternStore.Save(json, p); Check(PatternStore.Load(json).Fingerprint() == p.Fingerprint(), "version 3 JSON・全13音色往復");
    MidiCodec.Save(midi, p); var restored = MidiCodec.Load(midi).Pattern;
    Check(restored.Bars == 3 && restored.Bpm == 120 && restored.Notes.Select(n => (n.Instrument, n.Tick, n.Velocity)).OrderBy(x => x.Tick).SequenceEqual(p.Notes.Select(n => (n.Instrument, n.Tick, n.Velocity)).OrderBy(x => x.Tick)), "13音色MIDI往復・Muteに関係なく全音符出力");
    Check(InstrumentCatalog.All.Select(d => d.MidiNote).SequenceEqual(new[] { 36, 38, 42, 46, 39, 75, 45, 47, 48, 50, 41, 49, 51 }), "GM番号対応");
    p.Bpm = 1; Reject(() => MidiCodec.Save(midi, p), "BPM1 MIDI出力拒否");
    File.WriteAllBytes(midi, [1, 2, 3]); Reject(() => MidiCodec.Load(midi), "破損MIDI拒否");
    p = new Pattern { SchemaVersion = 1, Bpm = 91, GridDivision = 8, SelectedInstrument = Instrument.Perc, Fold = true };
    p.Tracks = p.Tracks.Take(6).ToList(); p.Tracks[1] = p.Tracks[1] with { Enabled = false }; p.Notes.Add(new(Guid.NewGuid(), Instrument.OpenHH, 4560, 83));
    string original = p.Fingerprint(); File.WriteAllText(json, original);
    var migrated = PatternStore.LoadWithMetadata(json);
    Check(migrated.Migrated && migrated.Pattern.SchemaVersion == 3 && migrated.Pattern.Tracks.Count == 13, "旧version 1を13音色へ移行");
    Check(migrated.Pattern.Notes.SequenceEqual(p.Notes) && !migrated.Pattern.Tracks[1].Enabled && migrated.Pattern.SelectedInstrument == Instrument.Perc && migrated.Pattern.GridDivision == 8 && migrated.Pattern.Fold, "移行で旧音符・選択・Mute・刻み保持");
    Check(migrated.Pattern.Tracks.Skip(6).All(t => t.Enabled) && File.ReadAllText(json) == original, "新7音色有効・旧ファイルは変更しない");
    File.WriteAllText(json, original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99")); Reject(() => PatternStore.Load(json), "未知版拒否");
    File.WriteAllText(json, original.Replace("\"OpenHH\"", "\"Unknown\"")); Reject(() => PatternStore.Load(json), "未知音色ID拒否");
    File.WriteAllText(json, "{}"); Reject(() => PatternStore.Load(json), "必要項目欠落拒否");
    p = new Pattern { SchemaVersion = 2, SelectedInstrument = Instrument.HighMiddleTom };
    p.Tracks = p.Tracks.Where(t=>t.Instrument is not (Instrument.LowTom or Instrument.LowMiddleTom or Instrument.HighTom)).ToList();
    p.Tracks[6] = p.Tracks[6] with { Enabled = false };
    p.Notes.Add(new(Guid.NewGuid(), Instrument.HighMiddleTom, 240, 76));
    original = p.Fingerprint().Replace("\"HighMiddleTom\"", "\"Tom\""); File.WriteAllText(json, original);
    migrated = PatternStore.LoadWithMetadata(json);
    Check(migrated.Migrated && migrated.Pattern.Tracks.Count == 13 && migrated.Pattern.SelectedInstrument == Instrument.HighMiddleTom && migrated.Pattern.Notes.SequenceEqual(p.Notes) && !migrated.Pattern.Tracks[(int)Instrument.HighMiddleTom].Enabled, "version 2 Tomの音符・選択・MuteをHighMiddleTomへ移行");
    Check(File.ReadAllText(json)==original && migrated.Pattern.Tracks.Where(t=>t.Instrument is Instrument.LowTom or Instrument.LowMiddleTom or Instrument.HighTom).All(t=>t.Enabled), "version 2の元ファイル保持と新Tom3行の追加");
    File.WriteAllText(json, original.Replace("\"Tom\"", "\"HighTom\"")); Reject(()=>PatternStore.Load(json), "旧版で新Tom名を拒否");
    p = new Pattern { Bars = 64, BeatsPerBar = 6 };
    for (int tick = 0; tick < p.Length; tick += 240) foreach (var d in InstrumentCatalog.All) p.Notes.Add(new(Guid.NewGuid(), d.Id, tick, 100));
    p.Validate(); Check(p.Notes.Count == 19968, "最大19,968音を検証"); PatternStore.Save(json, p); Check(PatternStore.Load(json).Notes.Count == 19968, "最大音符JSON往復");
    p.Notes.Add(new(Guid.NewGuid(), Instrument.Kick, 0, 100)); Reject(()=>p.Validate(), "音符上限超過拒否");

    string sourceKit = Path.Combine(workspace, "DrumEditor", "Assets", "Drums", "standard");
    var wavKit = SampleKitLoader.Load(sourceKit);
    Check(wavKit.Samples.Length == 13 && wavKit.Samples.All(s => s.Frames > 0), "同梱13 WAV準備");
    Check(wavKit.Samples.All(s => s.Stereo.Where((_, i) => i % 2 == 0).SequenceEqual(s.Stereo.Where((_, i) => i % 2 == 1))), "モノラルWAVを左右へ複製");
    var rate48 = SampleKitLoader.Load(sourceKit, 48000);
    Check(Math.Abs(rate48.Samples[8].Frames / 48000d - wavKit.Samples[8].Frames / 44100d) < 1d / 44100, "48kHzへ事前変換し長さ保持");
    string copiedKit = Path.Combine(dir, "kit"); Directory.CreateDirectory(copiedKit);
    foreach (string file in Directory.GetFiles(sourceKit)) File.Copy(file, Path.Combine(copiedKit, Path.GetFileName(file)));
    File.Delete(Path.Combine(copiedKit, "ride.wav")); Reject(() => SampleKitLoader.Load(copiedKit), "音源欠落に音色名を表示", "Ride");
    File.Copy(Path.Combine(sourceKit, "ride.wav"), Path.Combine(copiedKit, "ride.wav"));
    Check(SampleKitLoader.Load(copiedKit).Samples[9].Frames > 0, "音源修復後の再準備");
    string manifestPath = Path.Combine(copiedKit, "manifest.json"), manifest = File.ReadAllText(manifestPath);
    File.WriteAllText(manifestPath, manifest.Replace("\"ride.wav\"", "\"../ride.wav\"")); Reject(() => SampleKitLoader.Load(copiedKit), "フォルダー外パス拒否", "Ride");
    File.WriteAllText(manifestPath, manifest.Replace("\"gain\": 1.0", "\"gain\": 3.0")); Reject(() => SampleKitLoader.Load(copiedKit), "音源gain範囲検証");
    File.WriteAllText(manifestPath, manifest.Replace("\"id\": \"Ride\"", "\"id\": \"Crash\"")); Reject(() => SampleKitLoader.Load(copiedKit), "音源ID重複拒否");
    File.WriteAllText(manifestPath, manifest);
    var broken = File.ReadAllBytes(Path.Combine(copiedKit, "ride.wav")); broken[34] = 8; File.WriteAllBytes(Path.Combine(copiedKit, "ride.wav"), broken);
    Reject(() => SampleKitLoader.Load(copiedKit), "PCM8など非対応WAV拒否", "Ride");
    string stereoPath = Path.Combine(dir, "stereo.wav");
    using (var stream = File.Create(stereoPath)) using (var writer = new BinaryWriter(stream))
    {
        writer.Write("RIFF"u8.ToArray()); writer.Write(36 + 1000 * 4); writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(44100); writer.Write(176400); writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8.ToArray()); writer.Write(1000 * 4);
        for (int i = 0; i < 1000; i++) { writer.Write((short)16000); writer.Write((short)-8000); }
    }
    var stereo = SampleKitLoader.ReadWav(stereoPath); Check(stereo[0] > 0 && stereo[1] < 0 && Math.Abs(stereo[0] / stereo[1] + 2) < .01, "ステレオ左右を保持");
    p = new Pattern { Bpm = 120, Bars = 1 }; p.Notes.Add(new(Guid.NewGuid(), Instrument.HighMiddleTom, 240, 127));
    var signal = Render(p, 12000, ImpulseKit()); Check(Array.FindIndex(signal, x => x != 0) / 2 == 5513, "新規音色のサンプル精度発音位置");
    var boundarySource=new DrumRenderSource(ImpulseKit());boundarySource.Publish(p);boundarySource.Start(0);var beforeBoundary=new float[5513*2];boundarySource.RenderAdd(beforeBoundary,5513,0,true);var afterBoundary=new float[4];boundarySource.RenderAdd(afterBoundary,2,5513,true);Check(beforeBoundary.All(x=>x==0)&&afterBoundary[0]>.49f&&afterBoundary[2]==0,"ブロック終端の発音を次ブロックで1回だけ処理");
    var velocities=new List<float>();foreach(int velocityValue in new[]{1,64,127}){var velocityPattern=new Pattern{Bpm=120};velocityPattern.Notes.Add(new(Guid.NewGuid(),Instrument.Ride,0,velocityValue));velocities.Add(Render(velocityPattern,1,ImpulseKit())[0]);}
    Check(velocities[0]<velocities[1]&&velocities[1]<velocities[2]&&Math.Abs(velocities[1]/velocities[2]-64/127f)<.00001f,"強さ1・64・127の線形ゲイン");
    for (int bpm = 1; bpm <= 300; bpm += 299)
    {
        p.Bpm = bpm; var source = new DrumRenderSource(ImpulseKit()); source.Publish(p); source.Start(0);
        long expected = (long)Math.Round(240 * 60 * 44100 / (bpm * 960d), MidpointRounding.AwayFromZero);
        var block = new float[6]; source.RenderAdd(block, 3, expected - 1, true);
        Check(block[0] == 0 && block[2] > 0 && block[4] == 0, $"BPM{bpm}のブロック内オフセット");
    }
    p.Bpm = 120; var a = new DrumRenderSource(ImpulseKit()); var b = new DrumRenderSource(ImpulseKit()); a.Publish(p); b.Publish(p); a.Start(0); b.Start(0);
    var all = new float[200000 * 2]; a.RenderAdd(all, 200000, 0, true); var pieces = new float[all.Length];
    for (int frame = 0; frame < 200000; frame += 441) { int frames = Math.Min(441, 200000 - frame); var block = new float[frames * 2]; b.RenderAdd(block, frames, frame, true); Array.Copy(block, 0, pieces, frame * 2, block.Length); }
    Check(all.SequenceEqual(pieces), "ブロック境界・複数ループ一致");
    p = new Pattern { Bpm = 120, Bars = 1 }; p.Notes.Add(new(Guid.NewGuid(), Instrument.HiHat, 0, 127)); p.Notes.Add(new(Guid.NewGuid(), Instrument.OpenHH, 0, 127));
    var both = Render(p, 1000, ImpulseKit()); p.Notes.RemoveAll(n => n.Instrument == Instrument.OpenHH); Check(both.SequenceEqual(Render(p, 1000, ImpulseKit())), "同時ハイハットはClosed優先");
    p.Notes = [new(Guid.NewGuid(), Instrument.OpenHH, 0, 127), new(Guid.NewGuid(), Instrument.HiHat, 240, 127)];
    var choked = Render(p, 6000, ConstantKit(12000)); Check(choked[5605 * 2] == .125f, "OpenHHの2msチョーク完了");
    p.Notes = [new(Guid.NewGuid(), Instrument.Crash, 0, 127)]; var longSound = Render(p, 89000, ConstantKit(100000)); Check(longSound[88201 * 2] > .125f, "Crash残音がループ境界を越えて重なる");
    p.Notes = [new(Guid.NewGuid(), Instrument.Ride, 0, 127)]; Check(Render(p, 10, ImpulseKit(), solo: 1 << (int)Instrument.Kick).All(x => x == 0), "新規音色Solo除外");
    Check(Render(p, 10, ImpulseKit(), mask: 8191 ^ (1 << (int)Instrument.Ride)).All(x => x == 0), "新規音色Mute");
    var fadeSource = new DrumRenderSource(ConstantKit(12000)); fadeSource.Publish(p); fadeSource.Start(0); var warm = new float[2]; fadeSource.RenderAdd(warm, 1, 0, true); fadeSource.FadeStop(); var fade = new float[600]; fadeSource.RenderAdd(fade, 300, 1, false); Check(fade[0] > 0 && fade[440] == 0 && fadeSource.PositionFrames == 0, "停止時5msフェードと位置0");
    var changing = new DrumRenderSource(ImpulseKit()); p.Notes = [new(Guid.NewGuid(), Instrument.Kick, 0, 127)]; changing.Publish(p); changing.Start(0); changing.RenderAdd(new float[2], 1, 0, true);
    var next = p.Copy(); next.Notes = [new(Guid.NewGuid(), Instrument.Ride, 240, 127)]; changing.Publish(next);
    var edited = new float[190000]; changing.RenderAdd(edited, 95000, 1, true);
    Check(edited[(88200 - 1) * 2] == 0 && edited[(88200 + 5513 - 1) * 2] > 0, "編集中の変更を次ループから適用");
    var allocationSource = new DrumRenderSource(ImpulseKit()); allocationSource.Publish(next); allocationSource.Start(0); var reuse = new float[882]; allocationSource.RenderAdd(reuse, 441, 0, true);
    long before = GC.GetAllocatedBytesForCurrentThread(); for (int frame = 441; frame < 200000; frame += 441) { Array.Clear(reuse); allocationSource.RenderAdd(reuse, 441, frame, true); }
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "音声RenderAddでヒープ割当なし");
    var quiet = new float[] { .2f, -.1f, -.4f, .3f }; var quietBefore = quiet.ToArray();
    new StereoPeakLimiter(44100).Process(quiet, 2);
    Check(quiet.SequenceEqual(quietBefore), "リミッターは通常音量を変更しない");
    var overload = new float[] { 32, -16, -4, 2 }; var protect = new StereoPeakLimiter(44100); protect.Process(overload, 2);
    Check(overload.All(x => Math.Abs(x) <= StereoPeakLimiter.Ceiling) && Math.Abs(overload[0] / overload[1] + 2) < .00001, "大きなピークを抑え左右の比率を保持");
    var releaseSignal = Enumerable.Repeat(.2f, 44100 * 2).ToArray(); protect.Process(releaseSignal, 44100);
    Check(releaseSignal[0] < .02f && releaseSignal[^1] > .199f, "音割れ抑制の後に100msで滑らかに音量回復");
    p = new Pattern { Bpm = 300, Bars = 1, MetronomeEnabled = true };
    for (int tick = 0; tick < p.Length; tick += 240) foreach (var instrument in InstrumentCatalog.All) p.Notes.Add(new(Guid.NewGuid(), instrument.Id, tick, 127));
    var dense = Render(p, 44100 * 8, wavKit); float densePeak = dense.Max(x => Math.Abs(x));
    var wholeLimited = dense.ToArray(); new StereoPeakLimiter(44100).Process(wholeLimited, wholeLimited.Length / 2);
    var splitLimited = new float[dense.Length]; var splitLimiter = new StereoPeakLimiter(44100);
    for (int f = 0; f < dense.Length / 2; f += 441)
    {
        int count = Math.Min(441, dense.Length / 2 - f); var chunk = dense.AsSpan(f * 2, count * 2).ToArray();
        splitLimiter.Process(chunk, count); chunk.CopyTo(splitLimited, f * 2);
    }
    Check(wholeLimited.All(x => float.IsFinite(x) && Math.Abs(x) <= StereoPeakLimiter.Ceiling), "BPM300全13音色連打とメトロノームでも出力上限以内");
    Check(wholeLimited.SequenceEqual(splitLimited), "リミッターの効果は音声ブロック分割に依存しない");
    Console.WriteLine($"Dense mix raw peak {densePeak:F4}; protected peak {wholeLimited.Max(x => Math.Abs(x)):F4}");
    var limiterReuse = new float[882]; var allocationLimiter = new StereoPeakLimiter(44100); allocationLimiter.Process(limiterReuse, 441);
    before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) allocationLimiter.Process(limiterReuse, 441);
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "出力リミッターでヒープ割当なし");
    var atDefaultVolume = dense.ToArray(); new DeviceOutputStage(44100).Process(atDefaultVolume, atDefaultVolume.Length / 2, DeviceOutputStage.DefaultVolume);
    Check(atDefaultVolume.Zip(dense).All(pair => pair.First == pair.Second * .5f), "通常音量では高密度の実音源も波形を変えず50%へ線形減衰");
    Check(atDefaultVolume.Max(x => Math.Abs(x)) < StereoPeakLimiter.Ceiling, "最大強さ・BPM300連打にも出力の余裕を確保");
    var volumeStage = new DeviceOutputStage(44100); var constant = Enumerable.Repeat(.4f, 2000 * 2).ToArray(); volumeStage.Process(constant, 2000, 0);
    Check(constant[0] > .19f && constant[^1] == 0 && constant.Zip(constant.Skip(2)).All(pair => pair.First >= pair.Second), "音量を下げる時は滑らかに減衰してミュート");
    var raised = Enumerable.Repeat(.4f, 2000 * 2).ToArray(); volumeStage.Process(raised, 2000, 1);
    Check(raised[0] < .001 && Math.Abs(raised[^1] - .4f) < .00001, "音量を上げる時も滑らかに復帰");
    var volumeA = new DeviceOutputStage(44100); var volumeB = new DeviceOutputStage(44100);
    var rampWhole = Enumerable.Repeat(.3f, 4000).ToArray(); var rampParts = rampWhole.ToArray(); volumeA.Process(rampWhole, 2000, .2f);
    for (int startFrame = 0; startFrame < 2000; startFrame += 100) { var part = rampParts.AsSpan(startFrame * 2, 200).ToArray(); volumeB.Process(part, 100, .2f); part.CopyTo(rampParts, startFrame * 2); }
    Check(rampWhole.SequenceEqual(rampParts), "音量のフェードはバッファ境界で途切れない");
    before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) volumeStage.Process(limiterReuse, 441, .5f);
    Check(GC.GetAllocatedBytesForCurrentThread() == before, "全体音量とピーク抑制の処理でヒープ割当なし");
    if (args.Contains("--device-check"))
    {
        using var deviceAudio = new DrumAudio(wavKit) { Volume = 0 };
        deviceAudio.Publish(p); Thread.Sleep(150); deviceAudio.Play();
        using var stopLoad = new CancellationTokenSource();
        var load = Enumerable.Range(0, Math.Min(2, Math.Max(1, Environment.ProcessorCount - 1))).Select(_ => Task.Run(() => { while (!stopLoad.IsCancellationRequested) Thread.SpinWait(10000); })).ToArray();
        Thread.Sleep(8000); stopLoad.Cancel(); Task.WaitAll(load);
        Check(deviceAudio.Connected && deviceAudio.Playing && deviceAudio.BuffersWritten > 300, "実デバイスへの8秒連続出力がCPU負荷中も継続（消音）");
        Console.WriteLine($"DEVICE: {deviceAudio.BuffersWritten} buffers; underruns {deviceAudio.UnderrunCount}; longest fill {deviceAudio.LongestFillMilliseconds:F3} ms");
        Check(deviceAudio.UnderrunCount == 0, "実デバイスで音声バッファ不足0回");
        deviceAudio.Stop(); Thread.Sleep(120); Check(!deviceAudio.Playing, "通知方式で再生停止が反映");
        deviceAudio.Dispose(); deviceAudio.Dispose(); Check(!deviceAudio.Connected, "通知待ちの音声スレッドとデバイスを終了・二重解放なし");
    }
    if (args.Contains("--render-preview"))
    {
        void WritePreview(string name, float[] audio)
        {
            using var writer = new BinaryWriter(File.Create(Path.Combine(workspace, name)));
            writer.Write("RIFF"u8.ToArray()); writer.Write(36 + audio.Length * 2); writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(44100); writer.Write(176400); writer.Write((ushort)4); writer.Write((ushort)16);
            writer.Write("data"u8.ToArray()); writer.Write(audio.Length * 2);
            foreach (float value in audio) writer.Write((short)(Math.Clamp(value, -1, 1) * 32767));
        }
        var previewSource = new DrumRenderSource(wavKit); var previewAudio = new float[44100 * 3 * 5 * 2]; int cursor = 0;
        foreach (var instrument in new[] { Instrument.Clap, Instrument.LowTom, Instrument.LowMiddleTom, Instrument.HighMiddleTom, Instrument.HighTom })
        {
            previewSource.Preview(instrument); var chunk = new float[44100 * 3 * 2]; previewSource.RenderAdd(chunk, 44100 * 3, cursor, false);
            chunk.CopyTo(previewAudio, cursor * 2); cursor += 44100 * 3;
        }
        new DeviceOutputStage(44100).Process(previewAudio, previewAudio.Length / 2, DeviceOutputStage.DefaultVolume); WritePreview("preview-clap-toms.wav", previewAudio);
        var demoAudio = Render(InstrumentCatalog.CreateDemo(), 44100 * 12, wavKit); new DeviceOutputStage(44100).Process(demoAudio, demoAudio.Length / 2, DeviceOutputStage.DefaultVolume); WritePreview("preview-13tracks.wav", demoAudio);
    }
}
finally
{
    string resolved = Path.GetFullPath(dir);
    if (!resolved.StartsWith(testRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("DrumEditorTests-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup target");
    Directory.Delete(resolved, true);
}
Console.WriteLine($"{passed} checks passed");

