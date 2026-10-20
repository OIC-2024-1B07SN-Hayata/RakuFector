using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using DrumEditor.Domain;
using DrumEditor.Files;
using DrumEditor.Audio;
using DrumEditor.Controls;
namespace DrumEditor.Views;

public partial class DrumEditorView : UserControl
{
    readonly Editor editor = new();
    Pattern P => editor.Pattern;
    DrumAudio? audio;
    int solo;
    string? path, suggestedSaveName, midiFolder;
    string baseline;
    string audioError = "";
    bool refreshing, busy, preparingAudio, requestedPlay, syncingScroll, snapshot;
    readonly List<double> taps = [];
    double? lastTap;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly string kitDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Drums", "standard");
    Brush Accent => (Brush)FindResource("Accent");
    static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    bool IsPlaying => requestedPlay || audio?.Playing == true;

    public DrumEditorView()
    {
        InitializeComponent(); baseline = P.EditFingerprint(); EditorTitle = "ドラム編集 · 音声修正版";
        // Embedded editor: window size, title and lifetime belong to the unified shell.
        for (int meter = 3; meter <= 6; meter++)
        {
            int value = meter; var button = new Button { Content = $"{meter}/4", Tag = meter, Width = 64 };
            button.Click += (_, _) => ChangeMeter(value); Meters.Children.Add(button);
        }
        BuildNavigation();
        DrumGrid.Add += (instrument, tick) => Change(p =>
        {
            p.SelectedInstrument = instrument;
            var existing = p.Notes.FirstOrDefault(n => n.Instrument == instrument && n.Tick == tick);
            if (existing != null) { SelectNote(existing.Id); return; }
            var note = new Note(Guid.NewGuid(), instrument, tick, 100); p.Notes.Add(note); SetSelected(note.Id);
        });
        DrumGrid.Select += SelectNote; VelocityGrid.Select += SelectNote;
        DrumGrid.Move += (id, tick) => Change(p =>
        {
            int index = p.Notes.FindIndex(n => n.Id == id);
            if (index >= 0 && !p.Notes.Any(n => n.Id != id && n.Instrument == p.Notes[index].Instrument && n.Tick == tick))
                p.Notes[index] = p.Notes[index] with { Tick = tick };
        });
        VelocityGrid.Velocity += (id, velocity) => Change(p =>
        {
            int index = p.Notes.FindIndex(n => n.Id == id);
            if (index >= 0) p.Notes[index] = p.Notes[index] with { Velocity = velocity };
        });
        InitializeAdvancedEditing(); InitializeTrackpad(); InitializePolish(); BuildContextMenu(); Refresh();
        timer.Tick += (_, _) =>
        {
            if (requestedPlay && (audio?.Playing == true || audio?.Connected != true)) requestedPlay = false;
            foreach (var grid in Grids()) { grid.Playing = IsPlaying; grid.PlayTick = IsPlaying ? audio?.Tick ?? 0 : rangeLoop ? (loopFirst-1)*P.BeatsPerBar*960 : 0; grid.InvalidateVisual(); }
            UpdateTransport();
        };
        timer.Start();
        Loaded += async (_, _) => { if (audio is null && !preparingAudio) await PrepareAudio(); };
    }
    IEnumerable<DrumGrid> Grids() => [DrumGrid, BeatRuler, VelocityGrid];
    void BuildNavigation()
    {
        string[] labels = ["エフェクト", "チューナー", "コード検出", "ドラム編集", "フットスイッチ\n設定"];
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new NavigationIcon { Kind = i, Width = 40, Height = 38, Stroke = i == 3 ? Accent : Brush("#7D8692"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 20) });
            panel.Children.Add(new TextBlock { Text = labels[i], TextAlignment = TextAlignment.Center, FontSize = 14, Foreground = i == 3 ? Brush("#CDD2DA") : Brush("#7D8692") });
            var button = new Button { Content = panel, Height = 134, Margin = new Thickness(0), Padding = new Thickness(0), Background = i == 3 ? Brush("#0D0F12") : Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => NavigationRequested?.Invoke(new[] { "Tone", "Tuner", "Chord", "Drum", "Footswitch" }[index]);
            Navigation.Children.Add(button);
        }
    }
    void BuildContextMenu()
    {
        var menu = new ContextMenu();
        void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => Safe(action); menu.Items.Add(item); }
        Item("選択音符を削除   Delete", Delete); Item("元に戻す   Ctrl+Z", Undo); Item("やり直し   Ctrl+Y", Redo);
        menu.Items.Add(new Separator());
        Item("新規パターン", () => NewPattern(false)); Item("写真のデモパターンを開く", () => NewPattern(true));
        Item("保存   Ctrl+S", () => _ = Save(false)); Item("名前を付けて保存   Ctrl+Shift+S", () => _ = Save(true));
        Item("開く   Ctrl+O", () => Open(false));
        Item("MIDIフォルダーから読み込み", () => OpenMidiFolder()); Item("書き出し（MIDI / WAV）", ChooseExport);
        menu.Items.Add(new Separator()); Item("音源を再読み込み", () => _ = PrepareAudio());
        AddAdvancedMenu(menu); AddPolishMenu(menu);
        Item("操作ヘルプ", () => MessageBox.Show(Window.GetWindow(this), "空マス: 配置 / 音符クリック: 選択 / Delete: 削除\n上下左右ドラッグ: 移動\n下段の丸の上下ドラッグ: 強さ\n楽器名: 選択・試聴 / 番号: 有効切替 / S: Solo\nSpace: 再生・停止 / Esc: ドラッグ取消\nCtrl+Z/Y: Undo/Redo / Ctrl+S: 保存\nCtrl+O: 開く / Ctrl+A: 音符を全選択 / Ctrl+C・X・V: コピー・切り取り・貼り付け\n矢印キー: 選択音符を上下の楽器・左右の位置へ移動\n右クリック位置: 貼り付け先\nCtrl+I: MIDI読み込み / Ctrl+E: 書き出し形式選択\n再生中の配置変更は次のループで反映します。\n\n音源: Assets/Drums/standardのWAVを参照します。\nWAVを差し替えた後は停止し、音源を再読み込みしてください。", "ドラム編集の操作"));
        DrumGrid.ContextMenu = menu; VelocityGrid.ContextMenu = menu; RollArea.ContextMenu = menu;
    }
    async Task PrepareAudio()
    {
        if (preparingAudio || busy) return;
        if (IsPlaying) { MessageBox.Show(Window.GetWindow(this), "音源を再読み込みする前に停止してください。"); return; }
        preparingAudio = true; audioError = ""; UpdateTransport();
        try
        {
            var kit = await Task.Run(() => SampleKitLoader.Load(kitDirectory));
            var replacement = await Task.Run(() => new DrumAudio(kit));
            preparedKit=kit;var previous = audio; audio = replacement;
            audio.Volume = (float)(OutputVolume.Value / 100); ApplyLoopToAudio(); audio.Publish(P); audio.Solo(solo);
            if (previous != null) await Task.Run(previous.Dispose);
            Hint.Text = "13音色のWAVを準備しました · 楽器名をクリックして試聴できます。";
        }
        catch (Exception ex)
        {
            audioError = ex.Message; Hint.Text = "音源準備に失敗: " + ex.Message;
            if (!snapshot) MessageBox.Show(Window.GetWindow(this), ex.Message + "\n配置と保存は続けられます。修正後に右クリックから音源を再読み込みしてください。", "音源・音声の準備", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { preparingAudio = false; UpdateTransport(); }
    }
    void Safe(Action action)
    {
        if (busy) return;
        try { action(); } catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "操作できません", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    void Change(Action<Pattern> action) => Safe(() => { int meter=P.BeatsPerBar,bars=P.Bars;editor.Change(p=>{action(p);if(rangeLoop&&meter==p.BeatsPerBar&&bars==p.Bars)Timing.ValidateRange(p,(loopFirst-1)*p.BeatsPerBar*960,loopLast*p.BeatsPerBar*960);});if(meter!=P.BeatsPerBar||bars!=P.Bars)ResetRange();selectedNotes.RemoveWhere(id=>!P.Notes.Any(n=>n.Id==id));Refresh(); });
    void SetSelected(Guid? id) { selectedNotes.Clear();if(id is {} value)selectedNotes.Add(value);DrumGrid.Selected = VelocityGrid.Selected = id; }
    void SelectNote(Guid id)
    {
        var note = P.Notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return;
        SetSelected(id); Change(p => p.SelectedInstrument = note.Instrument);
    }
    void Refresh()
    {
        polishDirty=P.EditFingerprint()!=baseline;
        refreshing = true;
        try
        {
            BpmBox.Text = P.Bpm.ToString(); BarsText.Text = P.Bars.ToString();
            Division.SelectedIndex = P.GridDivision == 4 ? 0 : P.GridDivision == 8 ? 1 : 2;
            Tracks.Children.Clear(); Labels.Children.Clear();
            foreach (var d in InstrumentCatalog.All)
            {
                var instrument = d.Id;
                bool enabled = P.Tracks.First(t => t.Instrument == instrument).Enabled;
                var panel = new StackPanel { Margin = new Thickness(8, 2, 8, 4) };
                var title = new Button { Content = $"{(int)instrument + 1} {d.Name}", HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 17, Padding = new Thickness(3, 3, 3, 3), Height = 36, Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = $"{d.Name}を選択・試聴" };
                title.Height=24;title.Margin=new Thickness(0,0,0,2);
                title.Click += (_, _) => SelectTrack(instrument); panel.Children.Add(title);
                var buttons = new UniformGrid { Columns = 2 };
                var toggle = new Button { Content = ((int)instrument + 1).ToString(), Height = 48, Margin = new Thickness(3, 0, 3, 0), Background = enabled ? Accent : Brush("#191D23"), Foreground = enabled ? Brush("#09251C") : Brush("#7D8692"), FontWeight = FontWeights.Bold, ToolTip = "有効／無効" };
                toggle.Click += (_, _) => Change(p => { int index = p.Tracks.FindIndex(t => t.Instrument == instrument); p.Tracks[index] = p.Tracks[index] with { Enabled = !p.Tracks[index].Enabled }; });
                bool isSolo = (solo & (1 << (int)instrument)) != 0;
                var soloButton = new Button { Content = "S", Height = 48, Margin = new Thickness(3, 0, 3, 0), Background = isSolo ? Accent : Brush("#191D23"), Foreground = isSolo ? Brush("#09251C") : Brush("#CDD2DA"), ToolTip = "Solo（複数選択可）" };
                soloButton.Click += (_, _) => { solo ^= 1 << (int)instrument; audio?.Solo(solo); Refresh(); };
                toggle.Height=soloButton.Height=30;
                buttons.Children.Add(toggle); buttons.Children.Add(soloButton); panel.Children.Add(buttons);
                panel.Children.Add(CreateTrackVolumeSlider(instrument));
                Tracks.Children.Add(new Border { BorderBrush = Accent, BorderThickness = new Thickness(0, 4, 1, 0), Background = Brush("#191D23"), Child = panel });
            }
            foreach (var grid in Grids()) { grid.Pattern = P; grid.Refresh(); }
            foreach (var instrument in DrumGrid.Rows)
            {
                var label = new Button { Content = InstrumentCatalog.Get(instrument).Name, Height = DrumGrid.RowHeight, FontSize = 18, Margin = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(47, 0, 0, 0), BorderBrush = Brush("#2A2F37"), BorderThickness = new Thickness(0, 0, 0, 1), Foreground = instrument == P.SelectedInstrument ? Accent : Brush("#CDD2DA"), Background = Brush(instrument == P.SelectedInstrument ? "#32383F" : "#1B1F25") };
                label.Click += (_, _) => SelectTrack(instrument); Labels.Children.Add(label);
            }
            ApplyLoopToAudio();audio?.Publish(P); audio?.Solo(solo);
            EditorTitle = $"{P.Name}{(P.EditFingerprint() != baseline ? " *" : "")} — ドラム編集 13 TRACKS";
            EditorTitleChanged?.Invoke(EditorTitle);
            UpdateLayoutWidths();
        }
        finally { refreshing = false; }
        UpdateTransport();
    }
    void UpdateTransport()
    {
        if (BpmBox == null || BeatLights == null) return;
        bool stopped = !IsPlaying && !busy;
        TapButton.IsEnabled = BpmBox.IsEnabled = stopped;
        MinusBpm.IsEnabled = stopped && P.Bpm > 1; PlusBpm.IsEnabled = stopped && P.Bpm < 300;
        MinusBars.IsEnabled = stopped && P.Bars > 1; PlusBars.IsEnabled = stopped && P.Bars < 64;
        PlayButton.IsEnabled = stopped && !preparingAudio && audio?.Connected == true;
        foreach (Button button in Meters.Children) { button.IsEnabled = stopped; bool active = (int)button.Tag == P.BeatsPerBar; button.Background = active ? Accent : Brush("#191D23"); button.Foreground = active ? Brush("#09251C") : Brush("#CDD2DA"); }
        int beat = (int)((audio?.Tick ?? 0) / 960) % P.BeatsPerBar;
        if (BeatLights.Children.Count != P.BeatsPerBar) { BeatLights.Children.Clear(); for (int i = 0; i < P.BeatsPerBar; i++) BeatLights.Children.Add(new Ellipse { Width = 10, Height = 10, Margin = new Thickness(3), Fill = Brush("#2B3139") }); }
        for (int i = 0; i < BeatLights.Children.Count; i++) ((Ellipse)BeatLights.Children[i]).Fill = IsPlaying && i == beat ? Accent : Brush("#2B3139");
        MetroText.Foreground = P.MetronomeEnabled ? Accent : Brush("#7D8692");
        UpdateAdvancedTransport(stopped); UpdatePolishStatus();
        Status.Text = preparingAudio ? "● WAV音源を準備中…" : $"● {audio?.Status ?? "音声：未接続"}    ● {(audio != null ? "WAV · 13音色 · 44.1kHz" : "音源：未準備")}    {(IsPlaying ? "再生中" : "停止")}    {P.Notes.Count}音 · 選択{selectedNotes.Count}音{(audio?.UnderrunCount > 0 ? $"    バッファ不足: {audio.UnderrunCount}回" : "")}{(audioError.Length > 0 ? "    音源再読込失敗: " + audioError : "")}";
    }
    void LayoutChanged(object sender, SizeChangedEventArgs e) { if (DrumGrid != null && !refreshing) UpdateLayoutWidths(); }
    void UpdateLayoutWidths()
    {
        if (Scroller == null || CardScroller == null || DrumGrid == null) return;
        Tracks.Width = Math.Max(2340, CardScroller.ActualWidth);
        double velocityHeight = Math.Max(80, VelocityScroller.ActualHeight);
        if(Math.Abs(VelocityGrid.VelocityDisplayHeight-velocityHeight)>.05){VelocityGrid.VelocityDisplayHeight=velocityHeight;VelocityGrid.Refresh();}
        double verticalBar=Scroller.ComputedVerticalScrollBarVisibility==Visibility.Visible?SystemParameters.VerticalScrollBarWidth:0;
        RulerScroller.Margin = VelocityScroller.Margin = new Thickness(0, 0, verticalBar, 0);
        double horizontalBar=Scroller.ComputedHorizontalScrollBarVisibility==Visibility.Visible?SystemParameters.HorizontalScrollBarHeight:0;
        // Match the note viewport so the last label has the same scroll limit.
        LabelScroller.Margin=new Thickness(0,0,0,horizontalBar);
        double step = zoomPercent==0 ? Math.Max(.25, Math.Max(0, Scroller.ActualWidth - verticalBar - 1) / (P.Length / 240d)) : 30*zoomPercent/100d;
        double rowHeight=Math.Clamp(Math.Max(0,Scroller.ActualHeight-horizontalBar-1)/DrumGrid.Rows.Length,32,48);
        if(Math.Abs(DrumGrid.RowHeight-rowHeight)>.05){DrumGrid.RowHeight=rowHeight;DrumGrid.Refresh();foreach(Button label in Labels.Children)label.Height=rowHeight;}
        foreach (var grid in Grids()) if (Math.Abs(grid.Step - step) > .01) { grid.Step = step; grid.Refresh(); }
    }
    void RollScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (syncingScroll || LabelScroller == null || RulerScroller == null) return;
        syncingScroll = true;
        try { LabelScroller.ScrollToVerticalOffset(Scroller.VerticalOffset); RulerScroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset); VelocityScroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset); foreach (var grid in Grids()) grid.InvalidateVisual(); UpdateLayoutWidths(); }
        finally { syncingScroll = false; }
    }
    void LabelScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (syncingScroll || Scroller == null || e.VerticalChange == 0) return;
        syncingScroll = true; try { Scroller.ScrollToVerticalOffset(LabelScroller.VerticalOffset); } finally { syncingScroll = false; }
    }
    void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText != null) VolumeText.Text = $"{e.NewValue:0}%";
        if (audio != null) audio.Volume = (float)(e.NewValue / 100);
    }
    void SelectTrack(Instrument instrument) { SetSelected(null); Change(p => p.SelectedInstrument = instrument); audio?.Preview(instrument); }
    void CommitBpm(object sender, RoutedEventArgs e)
    {
        if (refreshing || IsPlaying) return;
        if (int.TryParse(BpmBox.Text, out int bpm) && bpm is >= 1 and <= 300) Change(p => p.Bpm = bpm);
        else { BpmBox.Text = P.Bpm.ToString(); MessageBox.Show(Window.GetWindow(this), "BPMは1～300の整数で入力してください。"); }
    }
    void BpmKey(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitBpm(sender, e); Keyboard.ClearFocus(); e.Handled = true; } }
    void BpmMinus(object sender, RoutedEventArgs e) => Change(p => p.Bpm = Math.Max(1, p.Bpm - 1));
    void BpmPlus(object sender, RoutedEventArgs e) => Change(p => p.Bpm = Math.Min(300, p.Bpm + 1));
    void Tap(object sender, RoutedEventArgs e)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (lastTap is { } last)
        {
            double interval = now - last;
            if (interval >= .2 && interval <= 60) { taps.Add(interval); if (taps.Count > 5) taps.RemoveAt(0); var sorted = taps.Order().ToArray(); double median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2; Change(p => p.Bpm = Math.Clamp((int)Math.Round(60 / median, MidpointRounding.AwayFromZero), 1, 300)); }
            else taps.Clear();
        }
        lastTap = now;
    }
    bool Confirm(string message) => MessageBox.Show(Window.GetWindow(this), message, "ドラム編集", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    void ChangeMeter(int meter)
    {
        if (IsPlaying || meter == P.BeatsPerBar) return;
        int old = P.BeatsPerBar, deleted = P.Notes.Count(n => n.Tick % (old * 960) >= meter * 960);
        if (deleted > 0 && !Confirm($"{deleted}音を削除しますか？")) return;
        Change(p => { p.Notes = p.Notes.Where(n => n.Tick % (old * 960) < meter * 960).Select(n => n with { Tick = Editor.RemapTick(n.Tick, old, meter) }).ToList(); p.BeatsPerBar = meter; });
    }
    void BarsMinus(object sender, RoutedEventArgs e) => ChangeBars(-1);
    void BarsPlus(object sender, RoutedEventArgs e) => ChangeBars(1);
    void ChangeBars(int delta)
    {
        if (IsPlaying) return;
        int bars = Math.Clamp(P.Bars + delta, 1, 64), length = bars * P.BeatsPerBar * 960, deleted = P.Notes.Count(n => n.Tick >= length);
        if (deleted > 0 && !Confirm($"末尾の{deleted}音を削除しますか？")) return;
        Change(p => { p.Bars = bars; p.Notes.RemoveAll(n => n.Tick >= length); });
    }
    void Metronome(object sender, RoutedEventArgs e) => Change(p => p.MetronomeEnabled = !p.MetronomeEnabled);
    void Fold(object sender, RoutedEventArgs e) => Change(p => p.Fold = !p.Fold);
    void Play(object sender, RoutedEventArgs e)
    {
        if (IsPlaying || preparingAudio || audio?.Connected != true) return;
        Safe(()=>{CommitBpm(BpmBox, e); audio.Publish(P); audio.Play(); requestedPlay = true; UpdateTransport();});
    }
    void Stop(object sender, RoutedEventArgs e) { requestedPlay = false; audio?.Stop(); foreach (var grid in Grids()) { grid.PlayTick = rangeLoop ? (loopFirst-1)*P.BeatsPerBar*960 : 0; grid.Playing = rangeLoop; grid.InvalidateVisual(); } }
    void DivisionChanged(object sender, SelectionChangedEventArgs e) { if (!refreshing && Division.SelectedIndex >= 0) Change(p => p.GridDivision = new[] { 4, 8, 16 }[Division.SelectedIndex]); }
    void Clear(object sender, RoutedEventArgs e) => Safe(()=>TargetedEdit(false));
    void Randomize(object sender, RoutedEventArgs e)
    {
        Safe(()=>TargetedEdit(true));
    }
    void Delete() { if(selectedNotes.Count>0){var ids=selectedNotes.ToHashSet();Change(p=>p.Notes.RemoveAll(n=>ids.Contains(n.Id)));SetSelected(null);Refresh();} }
    void Undo() { if (IsPlaying && editor.UndoChangesTiming) { Hint.Text = "テンポ・拍子・小節のUndoは停止後に操作してください。"; return; } bool timing=editor.UndoChangesTiming;editor.Undo();if(timing)ResetRange();PruneSelection();Refresh(); }
    void Redo() { if (IsPlaying && editor.RedoChangesTiming) { Hint.Text = "テンポ・拍子・小節のRedoは停止後に操作してください。"; return; } bool timing=editor.RedoChangesTiming;editor.Redo();if(timing)ResetRange();PruneSelection();Refresh(); }
    async Task<bool> Save(bool asNew)
    {
        if (busy) return false;
        string? target = path;
        if (asNew || target == null) { var dialog = new SaveFileDialog { Title = "JSON version 5で保存（旧版アプリでは開けません）", Filter = "ドラムパターン|*.drum.json", FileName = suggestedSaveName ?? P.Name + ".drum.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false; target = dialog.FileName; }
        var data = P.Copy(); busy = true; UpdateTransport();
        try { await Task.Run(() => PatternStore.Save(target, data)); path = target; suggestedSaveName = null; baseline = data.EditFingerprint(); DiscardRecovery();Refresh(); return true; }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "保存失敗"); return false; }
        finally { busy = false; UpdateTransport(); }
    }
    async Task<bool> MayDiscard()
    {
        if (P.EditFingerprint() == baseline) return true;
        var result = MessageBox.Show(Window.GetWindow(this), "変更を保存しますか？\nはい: 保存 / いいえ: 破棄 / キャンセル: 戻る", "未保存の変更", MessageBoxButton.YesNoCancel);
        if(result==MessageBoxResult.No){DiscardRecovery();return true;}return result == MessageBoxResult.Yes && await Save(false);
    }
    async void NewPattern(bool demo)
    {
        if (busy || IsPlaying) { MessageBox.Show(Window.GetWindow(this), "新規・デモを開く前に停止してください。"); return; }
        if (!await MayDiscard()) return;
        DiscardRecovery();ResetRange();editor.Load(demo ? InstrumentCatalog.CreateDemo() : new Pattern()); solo = 0; path = suggestedSaveName = null; SetSelected(null); baseline = demo ? "" : P.EditFingerprint(); Refresh();
    }
    async void Open(bool midi)
    {
        if (busy) return;
        if (IsPlaying) { MessageBox.Show(Window.GetWindow(this), "読み込み前に再生を停止してください。"); return; }
        if (!await MayDiscard()) return;
        var dialog = new OpenFileDialog { Filter = midi ? "MIDI|*.mid;*.midi" : "ドラムパターン|*.drum.json;*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await LoadFile(dialog.FileName, midi);
    }
    async Task LoadFile(string fileName, bool midi)
    {
        busy = true; IsEnabled = false; UpdateTransport();
        try
        {
            Pattern loaded; bool migrated = false;
            if (midi) { var result = await Task.Run(() => MidiCodec.Load(fileName)); loaded = result.Pattern; if (!Confirm(result.Report)) return; }
            else { var result = await Task.Run(() => PatternStore.LoadWithMetadata(fileName)); loaded = result.Pattern; migrated = result.Migrated; }
            DiscardRecovery();ResetRange();editor.Load(loaded); solo = 0; path = midi || migrated ? null : fileName; baseline = midi || migrated ? "" : P.EditFingerprint(); SetSelected(null);
            if (migrated) { suggestedSaveName = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(fileName)) + "-v5.drum.json"; Hint.Text = "旧版をversion 5へ移行しました。旧アプリでは開けないため、新しい名前で保存してください。"; }
            else suggestedSaveName = null;
            Refresh();
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "読込失敗"); }
        finally { busy = false; IsEnabled = true; UpdateTransport(); }
    }
    async void Export()
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Filter = "MIDI|*.mid", FileName = P.Name + ".mid" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await ExportMidiTo(dialog.FileName);
    }
    async Task ExportMidiTo(string fileName)
    {
        var data = P.Copy(); busy = true; UpdateTransport();
        try { await Task.Run(() => MidiCodec.Save(fileName, data)); Hint.Text = "MIDIを出力しました。楽器音量はJSON保存で保持します。"; }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "MIDI出力失敗"); }
        finally { busy = false; UpdateTransport(); }
    }
    void MidiImportClick(object sender, RoutedEventArgs e) => OpenMidiFolder();
    void ExportClick(object sender, RoutedEventArgs e) => ChooseExport();
    string? SelectMidiFolder()
    {
        var dialog = new OpenFolderDialog { Title = "MIDIフォルダーを選択", InitialDirectory = midiFolder ?? "", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return null;
        return midiFolder = dialog.FolderName;
    }
    async void OpenMidiFolder()
    {
        if (busy) return;
        if (IsPlaying) { MessageBox.Show(Window.GetWindow(this), "読み込み前に再生を停止してください。"); return; }
        if (!await MayDiscard()) return;
        string? directory = SelectMidiFolder(); if (directory == null) return;
        MidiFolderEntry[] entries;
        busy = true; IsEnabled = false; UpdateTransport();
        try { entries = await Task.Run(() => MidiFolder.List(directory)); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "MIDIフォルダー読込失敗"); return; }
        finally { busy = false; IsEnabled = true; UpdateTransport(); }
        if (entries.Length == 0) { MessageBox.Show(Window.GetWindow(this), "このフォルダーには.mid / .midiファイルがありません。", "MIDIフォルダー"); return; }
        var dialog = new MidiFolderDialog(directory, entries, false) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) await LoadFile(dialog.SelectedPath!, true);
    }
    async void ExportMidiFolder()
    {
        if (busy) return;
        string? directory = SelectMidiFolder(); if (directory == null) return;
        var dialog = new MidiFolderDialog(directory, [], true, P.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        if (System.IO.File.Exists(dialog.SelectedPath) && !Confirm("同じ名前のMIDIがあります。上書きしますか？")) return;
        await ExportMidiTo(dialog.SelectedPath!);
    }
    void Keys(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.Key == Key.Escape) { DrumGrid.Cancel(); VelocityGrid.Cancel(); SetSelected(null); if(!IsPlaying)ResetRange();Refresh();return; }
        if (e.OriginalSource is TextBox) return;
        if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is Slider or ComboBox)return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        Safe(() => { if (ctrl) { switch (e.Key) { case Key.A: SelectAllNotes(); break; case Key.C: CopySelectedNotes(); break; case Key.X: CopySelectedNotes(true); break; case Key.V: PasteSelectedNotes(); break; case Key.Z: Undo(); break; case Key.Y: Redo(); break; case Key.S: _ = Save((Keyboard.Modifiers & ModifierKeys.Shift) != 0); break; case Key.O: Open(false); break; case Key.I: OpenMidiFolder(); break; case Key.E: ChooseExport(); break; default: return; } e.Handled = true; } else if (e.Key == Key.Space) { if (IsPlaying) Stop(this, e); else Play(this, e); e.Handled = true; } else if(e.Key is Key.Left or Key.Right or Key.Up or Key.Down){MoveSelectedNotes(e.Key==Key.Left? -3840/P.GridDivision:e.Key==Key.Right?3840/P.GridDivision:0,e.Key==Key.Up?-1:e.Key==Key.Down?1:0);e.Handled=true;} else if (e.Key == Key.Delete) { Delete(); e.Handled = true; } });
    }
    public string EditorTitle { get; private set; } = "ドラム編集";
    public event Action<string>? NavigationRequested;
    public event Action<string>? EditorTitleChanged;

    public async Task<bool> CanCloseAsync()
    {
        if (busy || preparingAudio) return false;
        return await MayDiscard();
    }

    private bool disposed;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SaveUiPreferences();
        ShutdownPolish();
        timer.Stop();
        audio?.Dispose();
        DisposeTrackpad();
    }
}
