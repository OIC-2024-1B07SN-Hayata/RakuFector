using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DrumEditor.Controls;

public sealed class ValueDialog : Window
{
    public int Value { get; private set; }
    public ValueDialog(string title,string description,int initial,int minimum,int maximum,bool slider=false,Action<int>? preview=null)
    {
        DialogTheme.Apply(this);
        Title=title;Width=430;Height=230;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(23,26,31));Foreground=Brushes.LightGray;
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock{Text=description,TextWrapping=TextWrapping.Wrap});
        var text=new TextBox{Text=initial.ToString(),Height=36,Margin=new Thickness(0,10,0,10),FontSize=18};panel.Children.Add(text);
        if(slider){Height=290;var control=new Slider{Minimum=minimum,Maximum=maximum,Value=initial,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,12)};panel.Children.Add(control);control.ValueChanged+=(_,_)=>{text.Text=((int)control.Value).ToString();preview?.Invoke((int)control.Value);};text.TextChanged+=(_,_)=>{if(int.TryParse(text.Text,out int n)&&n>=minimum&&n<=maximum)control.Value=n;};var reset=new Button{Content="100%へ戻す"};panel.Children.Add(reset);reset.Click+=(_,_)=>control.Value=100;}
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var ok=new Button{Content="適用",IsDefault=true,MinWidth=90};var cancel=new Button{Content="キャンセル",IsCancel=true,MinWidth=110};buttons.Children.Add(ok);buttons.Children.Add(cancel);
        ok.Click+=(_,_)=>{if(!int.TryParse(text.Text,out int n)||n<minimum||n>maximum){MessageBox.Show(this,$"{minimum}～{maximum}の整数を入力してください。");return;}Value=n;DialogResult=true;};
    }
}
