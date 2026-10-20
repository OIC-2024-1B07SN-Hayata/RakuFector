using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrumEditor.Controls;
using DrumEditor.Domain;
using DrumEditor.Files;

namespace DrumEditor.Views;

public partial class DrumEditorView
{
    readonly HashSet<Guid> selectedNotes=[];
    int firstBar,lastBar,barAnchor,loopFirst,loopLast;
    bool rangeLoop,preferencesEnabled;
    double? restoredRatio;
    BarClipboard? barClipboard;
    Note[] noteClipboard=[];
    int editCursorTick;

    void InitializeAdvancedEditing()
    {
        DrumGrid.SelectedIds=VelocityGrid.SelectedIds=selectedNotes;
        DrumGrid.MultiSelect+=(id,toggle)=>
        {
            if(toggle){if(!selectedNotes.Add(id))selectedNotes.Remove(id);DrumGrid.Selected=VelocityGrid.Selected=selectedNotes.Count==1?selectedNotes.Single():null;}
            else if(!selectedNotes.Contains(id)||selectedNotes.Count==1)SelectNote(id);
            Refresh();
        };
        DrumGrid.BoxSelect+=ids=>{selectedNotes.Clear();selectedNotes.UnionWith(ids);DrumGrid.Selected=VelocityGrid.Selected=ids.Count==1?ids.Single():null;Refresh();};
        DrumGrid.CursorPosition+=tick=>editCursorTick=tick;
        DrumGrid.MoveAcross+=(delta,rows)=>MoveSelectedNotes(delta,rows);
        DrumGrid.MoveGroup+=delta=>Change(p=>EditingOperations.MoveSelection(p,selectedNotes,delta));
        BeatRuler.BarSelect+=(bar,extend)=>
        {
            if(IsPlaying||busy)return;
            if(!extend||barAnchor==0)barAnchor=bar;
            firstBar=Math.Min(barAnchor,bar);lastBar=Math.Max(barAnchor,bar);
            if(rangeLoop){try{Timing.ValidateRange(P,(firstBar-1)*P.BeatsPerBar*960,lastBar*P.BeatsPerBar*960);loopFirst=firstBar;loopLast=lastBar;}catch(Exception ex){rangeLoop=false;Hint.Text=ex.Message;}}
            Refresh();
        };
        preferencesEnabled=!snapshot&&!Environment.GetCommandLineArgs()[0].Contains("UiTests",StringComparison.OrdinalIgnoreCase);
        if(preferencesEnabled)RestoreUiPreferences();
        Loaded+=(_,_)=>
        {
            if(restoredRatio is {} ratio)
            {
                double available=RollArea.ActualHeight-42;
                if(available>=180){RollArea.RowDefinitions[1].Height=new GridLength(1,GridUnitType.Star);RollArea.RowDefinitions[3].Height=new GridLength(Math.Clamp(available*ratio,80,available-100));UpdateLayoutWidths();}
                restoredRatio=null;
            }
        };
    }
    void SelectAllNotes()
    {
        selectedNotes.Clear();selectedNotes.UnionWith(P.Notes.Select(n=>n.Id));
        DrumGrid.Selected=VelocityGrid.Selected=selectedNotes.Count==1?selectedNotes.Single():null;Refresh();
        Hint.Text=$"{selectedNotes.Count}音を全選択しました。";
    }
    void CopySelectedNotes(bool cut=false)
    {
        noteClipboard=EditingOperations.CopyNotes(P,selectedNotes);
        if(cut)Change(p=>p.Notes.RemoveAll(n=>selectedNotes.Contains(n.Id)));
        Hint.Text=$"{noteClipboard.Length}音を{(cut?"切り取り":"コピー")}しました。空いている位置を右クリックしてCtrl+Vで貼り付けます。";
    }
    void PasteSelectedNotes()
    {
        if(noteClipboard.Length==0){Hint.Text="音符を選択してCtrl+Cでコピーしてください。";return;}
        Guid[] ids=[];Change(p=>ids=EditingOperations.PasteNotes(p,noteClipboard,editCursorTick));
        if(ids.Length==0)return;
        selectedNotes.Clear();selectedNotes.UnionWith(ids);DrumGrid.Selected=VelocityGrid.Selected=ids.Length==1?ids[0]:null;
        editCursorTick=Math.Min(P.Length-240,editCursorTick+noteClipboard.Max(n=>n.Tick)+3840/P.GridDivision);DrumGrid.CursorTick=editCursorTick;Refresh();
    }
    void MoveSelectedNotes(int ticks,int rows)
    {
        if(selectedNotes.Count==0)return;
        Change(p=>EditingOperations.MoveSelection(p,selectedNotes,ticks,rows));
        var first=P.Notes.FirstOrDefault(n=>selectedNotes.Contains(n.Id));
        if(first!=null){P.SelectedInstrument=first.Instrument;editCursorTick=first.Tick;DrumGrid.CursorTick=editCursorTick;Refresh();double top=(int)first.Instrument*DrumGrid.RowHeight;if(top<Scroller.VerticalOffset)Scroller.ScrollToVerticalOffset(top);else if(top+DrumGrid.RowHeight>Scroller.VerticalOffset+Scroller.ViewportHeight)Scroller.ScrollToVerticalOffset(top+DrumGrid.RowHeight-Scroller.ViewportHeight);}
    }
    void PruneSelection(){selectedNotes.RemoveWhere(id=>!P.Notes.Any(n=>n.Id==id));if(DrumGrid.Selected is {} id&&!selectedNotes.Contains(id))DrumGrid.Selected=VelocityGrid.Selected=null;}
    void UpdateAdvancedTransport(bool stopped)
    {
        CopyBarsButton.IsEnabled=stopped&&firstBar>0;
        DuplicateBarsButton.IsEnabled=stopped;
        PasteBarsButton.IsEnabled=stopped&&firstBar>0&&barClipboard!=null;
        RangeLoopButton.IsEnabled=stopped&&(firstBar>0||rangeLoop);
        RangeLoopButton.Background=rangeLoop?Accent:Brush("#202329");RangeLoopButton.Foreground=rangeLoop?Brushes.Black:Brush("#CDD2DA");
        RangeText.Text=firstBar>0?$"選択 {firstBar}～{lastBar}小節{(rangeLoop?" · 範囲ループON":"")}":"拍見出しで小節を選択 · Shiftで範囲";
        foreach(var grid in Grids()){grid.RangeFirst=rangeLoop?loopFirst:firstBar;grid.RangeLast=rangeLoop?loopLast:lastBar;grid.InvalidateVisual();}
    }
    void ApplyLoopToAudio()=>audio?.SetLoopRange(rangeLoop?(loopFirst-1)*P.BeatsPerBar*960:0,rangeLoop?loopLast*P.BeatsPerBar*960:0);
    void ResetRange(){firstBar=lastBar=barAnchor=loopFirst=loopLast=0;rangeLoop=false;ApplyLoopToAudio();}
    void CopyBarsClick(object sender,RoutedEventArgs e)=>Safe(CopyBars);
    void PasteBarsClick(object sender,RoutedEventArgs e)=>Safe(()=>PasteBars(false));
    void DuplicateBarsClick(object sender,RoutedEventArgs e)=>Safe(()=>PasteBars(true));
    void RangeLoopClick(object sender,RoutedEventArgs e)=>Safe(()=>{if(IsPlaying)return;if(!rangeLoop&&firstBar==0)throw new InvalidDataException("小節を選択してください。");if(!rangeLoop)Timing.ValidateRange(P,(firstBar-1)*P.BeatsPerBar*960,lastBar*P.BeatsPerBar*960);rangeLoop=!rangeLoop;loopFirst=firstBar;loopLast=lastBar;Refresh();});
    void CopyBars(){if(IsPlaying)throw new InvalidDataException("小節編集の前に停止してください。");barClipboard=EditingOperations.CopyBars(P,firstBar,lastBar);Hint.Text=$"{barClipboard.Bars}小節をコピーしました。";UpdateTransport();}
    void PasteBars(bool duplicate)
    {
        if(IsPlaying)throw new InvalidDataException("小節編集の前に停止してください。");
        if(duplicate){RepeatBarsDialog();return;}
        EditingOperations.ValidateBars(P,firstBar,lastBar);
        var clip=duplicate?EditingOperations.CopyBars(P,firstBar,lastBar):barClipboard??throw new InvalidDataException("小節をコピーしてください。");
        int destination=duplicate?lastBar+1:firstBar,end=EditingOperations.PasteEnd(P,clip,destination),startTick=(destination-1)*P.BeatsPerBar*960,endTick=end*P.BeatsPerBar*960;
        int replaced=P.Notes.Count(n=>n.Tick>=startTick&&n.Tick<endTick);
        if((replaced>0||end>P.Bars)&&!Confirm($"{destination}～{end}小節へ貼り付けます。\n置き換える音符: {replaced}音\n変更後: {Math.Max(P.Bars,end)}小節\n続けますか？"))return;
        Change(p=>EditingOperations.PasteBars(p,clip,destination));
    }
    void AddAdvancedMenu(ContextMenu menu)
    {
        var bars=new MenuItem{Header="小節編集"};menu.Items.Add(bars);
        MenuItem Add(MenuItem parent,string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>Safe(action);parent.Items.Add(item);return item;}
        var copy=Add(bars,"小節をコピー",CopyBars);var paste=Add(bars,"小節を貼り付け",()=>PasteBars(false));var duplicate=Add(bars,"小節を複製",()=>PasteBars(true));
        var notesMenu=new MenuItem{Header="音符編集"};menu.Items.Add(notesMenu);
        Add(notesMenu,"全選択 (Ctrl+A)",SelectAllNotes);Add(notesMenu,"コピー (Ctrl+C)",()=>CopySelectedNotes());Add(notesMenu,"切り取り (Ctrl+X)",()=>CopySelectedNotes(true));Add(notesMenu,"貼り付け (Ctrl+V)",PasteSelectedNotes);
        var strength=new MenuItem{Header="選択音符の強さ"};menu.Items.Add(strength);Add(strength,"同じ値に設定",()=>EditVelocities(false));Add(strength,"増減",()=>EditVelocities(true));
        menu.Opened+=(_,_)=>{bars.IsEnabled=!IsPlaying;copy.IsEnabled=firstBar>0;duplicate.IsEnabled=true;paste.IsEnabled=barClipboard!=null&&firstBar>0;strength.IsEnabled=selectedNotes.Count>0;};
    }
    void RepeatBarsDialog()
    {
        int sourceFirst=firstBar>0?firstBar:1,sourceLast=lastBar>0?lastBar:sourceFirst;
        var form=new OptionsDialog("指定小節まで繰り返し複製"){Owner=Window.GetWindow(this)};
        form.Text("コピー元の範囲を、複製先の終了小節まで繰り返します。最後の繰り返しは指定した終了小節で区切ります。");
        form.Number("sourceFirst","コピー元 開始小節",sourceFirst,1,P.Bars);
        form.Number("sourceLast","コピー元 終了小節",sourceLast,1,P.Bars);
        int destination=Math.Min(64,sourceLast+1);
        form.Number("first","複製先 開始小節",destination,1,64);
        form.Number("last","何小節まで複製するか",Math.Min(64,Math.Max(P.Bars,destination+sourceLast-sourceFirst)),1,64);
        if(form.ShowDialog()!=true)return;
        var clip=EditingOperations.CopyBars(P,form.Number("sourceFirst"),form.Number("sourceLast"));
        int first=form.Number("first"),last=form.Number("last");
        if(last<first)throw new InvalidDataException("複製先の終了小節は開始小節以降にしてください。");
        int start=(first-1)*P.BeatsPerBar*960,end=last*P.BeatsPerBar*960;
        int replaced=P.Notes.Count(n=>n.Tick>=start&&n.Tick<end);
        var preview=P.Copy();EditingOperations.RepeatBars(preview,clip,first,last);preview.Validate();
        if(!Confirm($"コピー元: {form.Number("sourceFirst")}～{form.Number("sourceLast")}小節\n複製先: {first}～{last}小節\n置換する音符: {replaced}音\n変更後: {preview.Bars}小節\nまとめて複製しますか？（Undo1回で戻せます）"))return;
        Change(p=>EditingOperations.RepeatBars(p,clip,first,last));
    }
    void EditVelocities(bool relative)
    {
        if(selectedNotes.Count==0)return;
        var dialog=new ValueDialog("選択音符の強さ",relative?"全選択音符の強さを増減します。":"全選択音符を同じ強さに設定します。",relative?0:100,relative?-126:1,relative?126:127){Owner=Window.GetWindow(this)};
        if(dialog.ShowDialog()!=true)return;
        int limited=0;Change(p=>limited=EditingOperations.SetVelocities(p,selectedNotes,dialog.Value,relative));Hint.Text=$"{selectedNotes.Count}音の強さを変更しました。上限・下限: {limited}音";
    }
    void EditTrackVolume(Instrument instrument)
    {
        if(busy)return;
        var original=P.Tracks.First(t=>t.Instrument==instrument);
        var dialog=new ValueDialog(InstrumentCatalog.Get(instrument).Name+" 音量","楽器の音量 0～100%　音符の強さは変わりません。",original.VolumePercent,0,100,true,value=>
        {
            var preview=P.Copy();int index=preview.Tracks.FindIndex(t=>t.Instrument==instrument);preview.Tracks[index]=preview.Tracks[index] with{VolumePercent=value};audio?.SetTrackVolumes(preview);
        }){Owner=Window.GetWindow(this)};
        bool accepted=dialog.ShowDialog()==true;
        audio?.SetTrackVolumes(P);
        if(accepted)Change(p=>{int index=p.Tracks.FindIndex(t=>t.Instrument==instrument);p.Tracks[index]=p.Tracks[index] with{VolumePercent=dialog.Value};});
    }
    Grid CreateTrackVolumeSlider(Instrument instrument)
    {
        var row=new Grid{Height=24,Margin=new Thickness(3,2,3,0)};
        row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});
        var text=new TextBlock{Text=P.Tracks.First(t=>t.Instrument==instrument).VolumePercent+"%",FontSize=12,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        Grid.SetColumn(text,1);row.Children.Add(text);
        var slider=new Slider{Minimum=0,Maximum=100,Value=P.Tracks.First(t=>t.Instrument==instrument).VolumePercent,TickFrequency=1,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center,ToolTip=InstrumentCatalog.Get(instrument).Name+" 音量 0～100%"};
        System.Windows.Automation.AutomationProperties.SetName(slider,InstrumentCatalog.Get(instrument).Name+"の音量");
        bool dragging=false;
        void Commit(){int value=(int)slider.Value;if(P.Tracks.First(t=>t.Instrument==instrument).VolumePercent!=value)Change(p=>{int index=p.Tracks.FindIndex(t=>t.Instrument==instrument);p.Tracks[index]=p.Tracks[index] with{VolumePercent=value};});}
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,new System.Windows.Controls.Primitives.DragStartedEventHandler((_,_)=>dragging=true));
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,e)=>{dragging=false;if(e.Canceled){audio?.SetTrackVolumes(P);Refresh();}else Commit();}));
        slider.ValueChanged+=(_,_)=>
        {
            text.Text=((int)slider.Value)+"%";
            if(dragging){var preview=P.Copy();int index=preview.Tracks.FindIndex(t=>t.Instrument==instrument);preview.Tracks[index]=preview.Tracks[index] with{VolumePercent=(int)slider.Value};audio?.SetTrackVolumes(preview);}
            else Commit();
        };
        row.Children.Add(slider);return row;
    }
    void RestoreUiPreferences()
    {
        var settings = UiSettingsStore.Load(UiSettingsStore.DefaultPath);
        if (settings is null) return;
        zoomPercent = ZoomValues.Contains(settings.ZoomPercent) ? settings.ZoomPercent : 100;
        restoredRatio = settings.VelocityRatio;
        midiFolder = Directory.Exists(settings.MidiFolder) ? settings.MidiFolder : null;
    }
    void SaveUiPreferences()
    {
        if (!preferencesEnabled || !IsLoaded) return;
        try
        {
            var window = Window.GetWindow(this);
            var bounds = window?.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window?.RestoreBounds ?? new Rect(0, 0, 1600, 960);
            double total = RollArea.RowDefinitions[1].ActualHeight + RollArea.RowDefinitions[3].ActualHeight;
            UiSettingsStore.Save(UiSettingsStore.DefaultPath, new(bounds.Left, bounds.Top,
                Math.Max(1000, bounds.Width), Math.Max(700, bounds.Height),
                window?.WindowState == WindowState.Maximized,
                total > 0 ? RollArea.RowDefinitions[3].ActualHeight / total : .3,
                midiFolder, zoomPercent));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
