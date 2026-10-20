using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using DrumEditor.Audio;
using DrumEditor.Controls;
using DrumEditor.Domain;
using DrumEditor.Files;
namespace DrumEditor.Views;
public partial class DrumEditorView
{
    static readonly int[] ZoomValues=[0,25,50,75,100,150,200,300,400];
    int zoomPercent=100;
    SampleKit? preparedKit;
    TextBlock? polishState,zoomLabel;
    readonly DispatcherTimer recoveryTimer=new(){Interval=TimeSpan.FromSeconds(30)};
    readonly string recoverySession=Guid.NewGuid().ToString("N");
    string recoveryFingerprint="",recoveryStatus="復旧 待機";
    string? restoredRecoverySession;
    bool recoveryBusy,recoveryClosing,polishDirty;
    FileStream? recoveryLock;
    Task recoveryTask=Task.CompletedTask;
    void InitializePolish()
    {
        var toolbar=(Panel)RangeText.Parent;
        void Button(string text,Action action){var b=new Button{Content=text,FontSize=12,Padding=new Thickness(6,2,6,2),Margin=new Thickness(2)};b.Click+=(_,_)=>Safe(action);toolbar.Children.Add(b);}
        // Put the explanatory selection text in the persistent status line to free toolbar space.
        RangeText.Visibility=Visibility.Collapsed;
        Button("−",()=>ZoomAt(-1));zoomLabel=new TextBlock{Text="100%",VerticalAlignment=VerticalAlignment.Center,Width=40};toolbar.Children.Add(zoomLabel);Button("＋",()=>ZoomAt(1));Button("全体",()=>SetZoom(0));
        Button("音符数値",EditNumeric);Button("プリセット",Presets);Button("Swing",EditSwing);
        polishState=new TextBlock{FontSize=11,Margin=new Thickness(12,0,12,0),TextTrimming=TextTrimming.CharacterEllipsis};
        var row=(Grid)Hint.Parent;var line=new DockPanel{LastChildFill=true};Grid.SetRow(line,5);row.Children.Remove(Hint);Hint.Margin=new Thickness(12,2,12,0);DockPanel.SetDock(Hint,Dock.Top);line.Children.Add(Hint);line.Children.Add(polishState);row.Children.Add(line);
        row.RowDefinitions[5].Height=new GridLength(48);
        recoveryTimer.Tick+=(_,_)=>{if(!busy&&!recoveryBusy&&!recoveryClosing)recoveryTask=BackupRecovery();};
        Loaded+=(_,_)=>{if(preferencesEnabled){try{Directory.CreateDirectory(RecoveryStore.DefaultDirectory);recoveryLock=new FileStream(System.IO.Path.Combine(RecoveryStore.DefaultDirectory,recoverySession+".active"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){ }OfferRecovery();recoveryTimer.Start();}};
    }
    void AddPolishMenu(ContextMenu menu)
    {
        void Add(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>Safe(action);menu.Items.Add(item);}
        Add("選択音符の数値編集",EditNumeric);Add("リズムプリセット",Presets);Add("スウィング",EditSwing);
    }
    void UpdatePolishStatus()
    {
        if(polishState==null)return;
        if(zoomLabel!=null)zoomLabel.Text=zoomPercent==0?"全体":zoomPercent+"%";
        polishState.Text=$"{InstrumentCatalog.Get(P.SelectedInstrument).Name} · 選択{selectedNotes.Count}音 · {RangeText.Text} · Swing {P.SwingPercent}% · {(polishDirty?"未保存":"保存済み")} · {(audio?.WaitingForLoop==true?"次のループで反映":"反映済み")} · {recoveryStatus}";
    }
    void SetZoom(int value,double? anchor=null)
    {
        double x=anchor??Scroller.ViewportWidth/2,tick=(Scroller.HorizontalOffset+x)/DrumGrid.Step*240;
        zoomPercent=value;UpdateLayoutWidths();Scroller.UpdateLayout();Scroller.ScrollToHorizontalOffset(Math.Max(0,tick/240*DrumGrid.Step-x));UpdatePolishStatus();
    }
    void ZoomAt(int direction,double? anchor=null){int index=Array.IndexOf(ZoomValues,zoomPercent);SetZoom(ZoomValues[Math.Clamp(index+direction,0,ZoomValues.Length-1)],anchor);}
    void EditNumeric()
    {
        if(selectedNotes.Count!=1){Hint.Text="1音を選択してください。複数音符の強さは右クリックから変更できます。";return;}
        var n=P.Notes.Single(n=>selectedNotes.Contains(n.Id));var form=new OptionsDialog("音符の数値編集"){Owner=Window.GetWindow(this)};
        form.Text(InstrumentCatalog.Get(n.Instrument).Name+"　位置は240tick単位。微調整は発音だけをずらします。");
        form.Number("bar","小節",n.Tick/(P.BeatsPerBar*960)+1,1,P.Bars);form.Number("beat","拍",n.Tick/960%P.BeatsPerBar+1,1,P.BeatsPerBar);form.Number("tick","拍内tick",n.Tick%960,0,959);form.Number("velocity","強さ",n.Velocity,1,127);form.Number("offset","発音の微調整tick",n.TimingOffsetTick,-120,120);
        if(form.ShowDialog()==true)Change(p=>PolishOperations.EditNote(p,n.Id,form.Number("bar"),form.Number("beat"),form.Number("tick"),form.Number("velocity"),form.Number("offset")));
    }
    void EditSwing()
    {
        if(IsPlaying){Hint.Text="スウィング変更の前に停止してください。";return;}
        var form=new ValueDialog("スウィング","50%で均等、75%で8分裏が720tickになります。",P.SwingPercent,50,75){Owner=Window.GetWindow(this)};
        if(form.ShowDialog()==true)Change(p=>p.SwingPercent=form.Value);
    }
    void TargetedEdit(bool random)
    {
        var form=new OptionsDialog(random?"対象を指定してRandom":"対象を指定してClear"){Owner=Window.GetWindow(this)};
        form.Choice("instrument","対象楽器",["全楽器","選択中の楽器","チェックした楽器","未チェックの楽器"]);form.Choice("range","対象範囲",firstBar>0?["全体","選択小節"]:["全体"]);
        form.Text($"「未チェックの楽器」では、下でチェックした楽器を保護し、チェックしていない楽器だけを{(random?"Random":"Clear")}します。");
        foreach(var d in InstrumentCatalog.All)form.Check(d.Name,d.Name,d.Id==P.SelectedInstrument);
        if(random)form.Number("seed","乱数シード",Random.Shared.Next(),0,int.MaxValue);
        if(form.ShowDialog()!=true)return;
        var instruments=PolishOperations.ResolveInstruments(form.Choice("instrument"),P.SelectedInstrument,InstrumentCatalog.All.Where(d=>form.Check(d.Name)).Select(d=>d.Id).ToHashSet());
        if(instruments.Count==0)throw new InvalidDataException(form.Choice("instrument")==3?"未チェックの楽器がありません。対象にする楽器のチェックを外してください。":"楽器を選択してください。");int first=form.Choice("range")==1?firstBar:1,last=form.Choice("range")==1?lastBar:P.Bars;
        int count=P.Notes.Count(n=>instruments.Contains(n.Instrument)&&n.Tick>=(first-1)*P.BeatsPerBar*960&&n.Tick<last*P.BeatsPerBar*960);
        if(!Confirm($"{string.Join(", ",instruments.Select(i=>InstrumentCatalog.Get(i).Name))}\n{first}～{last}小節 · 対象{count}音を{(random?"再配置":"削除")}しますか？"))return;
        int seed=random?form.Number("seed"):0;Change(p=>PolishOperations.Target(p,instruments,first,last,random,seed));if(random)Hint.Text=$"Random seed: {seed}";
    }
    void Presets()
    {
        var form=new OptionsDialog("リズムプリセット"){Owner=Window.GetWindow(this)};form.Choice("preset","4/4 · 1小節 · 既存のドラム音源",RhythmPresets.All.Select(r=>r.Name+" / "+string.Join(" ",r.Pattern.Notes.Select(n=>n.Instrument).Distinct())).ToArray());form.Choice("scope","適用先",firstBar>0?["全体を置換","選択小節"]:["全体を置換"]);form.Check("tempo","推奨BPM120も適用");DrumAudio? preview=null;
        form.Action("試聴",()=>{if(IsPlaying)throw new InvalidDataException("停止してから試聴してください。");if(preparedKit==null)throw new InvalidDataException("音源を準備してください。");preview?.Dispose();var data=RhythmPresets.All[form.Choice("preset")].Pattern.Copy();data.Bpm=P.Bpm;data.Tracks=P.Tracks.ToList();preview=new DrumAudio(preparedKit){Volume=(float)(OutputVolume.Value/100)};preview.Publish(data);preview.Play();});form.Action("試聴停止",()=>{preview?.Dispose();preview=null;});
        bool accepted;try{accepted=form.ShowDialog()==true;}finally{preview?.Dispose();}
        if(!accepted)return;if(IsPlaying)throw new InvalidDataException("停止してから適用してください。");var preset=RhythmPresets.All[form.Choice("preset")];bool partial=form.Choice("scope")==1;
        if(!Confirm($"{preset.Name}を{(partial?$"{firstBar}～{lastBar}小節":$"全体（{P.Bars}小節から1小節へ）")}に適用します。\n変更前{P.Notes.Count}音 · プリセット{preset.Pattern.Notes.Count}音/小節\n続けますか？"))return;
        Change(p=>RhythmPresets.Apply(p,preset,partial?firstBar:null,partial?lastBar:null,form.Check("tempo")));
    }
    void ChooseExport()
    {
        if(busy)return;var form=new ExportFormatDialog{Owner=Window.GetWindow(this)};if(form.ShowDialog()!=true)return;if(form.SelectedFormat==ExportFormat.Midi)ExportMidiFolder();else if(form.SelectedFormat==ExportFormat.Wav)ExportWav();
    }
    async void ExportWav()
    {
        if(busy)return;if(IsPlaying){Hint.Text="停止してからWAVを書き出してください。";return;}if(preparedKit==null){Hint.Text="音源を準備してください。";return;}
        var form=new OptionsDialog("WAV書き出し"){Owner=Window.GetWindow(this)};form.Text("44.1kHz / 16bit / stereo　Mute・Solo・楽器音量・全体音量を反映します。");form.Choice("range","出力範囲",firstBar>0?["全体","選択小節"]:["全体"]);form.Number("repeat","繰り返し回数",1,1,16);form.Check("tail","末尾の残音を含める",true);form.Check("metro","メトロノームを含める");if(form.ShowDialog()!=true)return;
        var dialog=new SaveFileDialog{Filter="WAV音声|*.wav",FileName=Files.MidiFolder.SuggestedName(P.Name).Replace(".mid",".wav")};if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        var data=P.Copy();var options=new WavOptions(form.Choice("range")==1?firstBar:1,form.Choice("range")==1?lastBar:P.Bars,form.Number("repeat"),form.Check("tail"),form.Check("metro"),solo,(float)(OutputVolume.Value/100));
        try{WavExporter.Frames(data,preparedKit,options);}catch(Exception ex){MessageBox.Show(Window.GetWindow(this),ex.Message,"WAV出力設定");return;}
        using var cancel=new CancellationTokenSource();var panel=new StackPanel{Margin=new Thickness(20)};var progress=new ProgressBar{Minimum=0,Maximum=1,Height=24};panel.Children.Add(progress);var stop=new Button{Content="キャンセル"};stop.Click+=(_,_)=>cancel.Cancel();panel.Children.Add(stop);var status=new Window{Title="WAV書き出し中",Width=400,Height=160,Owner=Window.GetWindow(this),Content=panel,WindowStartupLocation=WindowStartupLocation.CenterOwner};DialogTheme.Apply(status);status.Closing+=(_,_)=>cancel.Cancel();
        busy=true;IsEnabled=false;UpdateTransport();status.Show();try{await Task.Run(()=>WavExporter.Save(dialog.FileName,data,preparedKit,options,cancel.Token,new Progress<double>(v=>Dispatcher.InvokeAsync(()=>progress.Value=v))));Hint.Text="WAVを書き出しました。";}catch(OperationCanceledException){Hint.Text="WAV書き出しを取り消しました。";}catch(Exception ex){MessageBox.Show(status,ex.Message,"WAV出力失敗");}finally{status.Close();busy=false;IsEnabled=true;UpdateTransport();}
    }
    async Task BackupRecovery()
    {
        if(P.EditFingerprint()==baseline||P.EditFingerprint()==recoveryFingerprint)return;
        var data=P.Copy();string fingerprint=data.EditFingerprint();recoveryBusy=true;
        try{await Task.Run(()=>RecoveryStore.Save(RecoveryStore.DefaultDirectory,recoverySession,path,data)).ConfigureAwait(false);recoveryFingerprint=fingerprint;if(restoredRecoverySession is {} oldSession){RecoveryStore.Clear(RecoveryStore.DefaultDirectory,oldSession);restoredRecoverySession=null;}recoveryStatus="復旧保存 "+DateTime.Now.ToString("HH:mm:ss");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){recoveryStatus="復旧保存に失敗";}
        finally{recoveryBusy=false;}
    }
    void DiscardRecovery(){recoveryFingerprint="";if(!preferencesEnabled)return;try{recoveryTask.GetAwaiter().GetResult();RecoveryStore.Clear(RecoveryStore.DefaultDirectory,recoverySession);if(restoredRecoverySession is {} oldSession){RecoveryStore.Clear(RecoveryStore.DefaultDirectory,oldSession);restoredRecoverySession=null;}recoveryStatus="復旧 待機";}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){recoveryStatus="復旧整理に失敗";}}
    void ShutdownPolish(){recoveryClosing=true;recoveryTimer.Stop();DiscardRecovery();recoveryLock?.Dispose();if(preferencesEnabled)try{File.Delete(System.IO.Path.Combine(RecoveryStore.DefaultDirectory,recoverySession+".active"));}catch(IOException){ }}
    void OfferRecovery()
    {
        try
        {
            var records=RecoveryStore.List(RecoveryStore.DefaultDirectory);if(records.Length==0)return;var form=new OptionsDialog("前回の編集を復元"){Owner=Window.GetWindow(this)};form.Choice("record","復旧できる編集",records.Select(r=>$"{r.SavedUtc.ToLocalTime():g} {r.Pattern.Name} {r.Pattern.Notes.Count}音").ToArray());form.Choice("action","操作",["復元","選択セッションを破棄","後で"]);if(form.ShowDialog()!=true||form.Choice("action")==2)return;
            var chosen=records[form.Choice("record")];if(form.Choice("action")==0){ResetRange();editor.Load(chosen.Pattern);path=null;baseline="";SetSelected(null);Refresh();Hint.Text="復元しました。元ファイルは変更していません。";restoredRecoverySession=chosen.Session;recoveryTask=BackupRecovery();}else RecoveryStore.Clear(RecoveryStore.DefaultDirectory,chosen.Session);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){recoveryStatus="復旧一覧を取得できません";}
    }
}
