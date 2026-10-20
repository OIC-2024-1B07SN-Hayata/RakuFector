using System.Windows;
using System.Windows.Controls;
namespace DrumEditor.Controls;
public enum ExportFormat { Midi, Wav }
public sealed class ExportFormatDialog : Window
{
    public ExportFormat? SelectedFormat { get; private set; }
    public ExportFormatDialog()
    {
        Title="書き出し形式を選択";Width=530;Height=370;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;DialogTheme.Apply(this);
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock{Text="どの形式で書き出しますか？",FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});
        void Add(string title,string description,ExportFormat format)
        {
            var content=new StackPanel();content.Children.Add(new TextBlock{Text=title,FontSize=18,FontWeight=FontWeights.Bold});content.Children.Add(new TextBlock{Text=description,FontSize=12,Margin=new Thickness(0,5,0,0)});
            var button=new Button{Content=content,HorizontalContentAlignment=HorizontalAlignment.Left,Height=80,Tag=format};button.Click+=(_,_)=>{SelectedFormat=format;DialogResult=true;};panel.Children.Add(button);
        }
        Add("MIDIを書き出す","音符データを保存して、ほかの音楽ソフトで編集します。",ExportFormat.Midi);
        Add("WAVを書き出す","現在のドラム音源と音量を反映した音声を保存します。",ExportFormat.Wav);
        panel.Children.Add(new Button{Content="キャンセル",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right});
    }
}
