using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DrumEditor.Controls;
public sealed class OptionsDialog : Window
{
    readonly StackPanel fields=new(){Margin=new Thickness(18)};
    readonly Dictionary<string,TextBox> numbers=[];
    readonly Dictionary<string,(int Min,int Max)> limits=[];
    readonly Dictionary<string,ComboBox> choices=[];
    readonly Dictionary<string,CheckBox> checks=[];
    public OptionsDialog(string title)
    {
        DialogTheme.Apply(this);
        Title=title;Width=540;Height=560;MinWidth=430;MinHeight=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(23,26,31));Foreground=Brushes.LightGray;
        var root=new DockPanel{LastChildFill=true};Content=root;
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var apply=new Button{Content="適用",IsDefault=true,MinWidth=100};buttons.Children.Add(apply);buttons.Children.Add(new Button{Content="キャンセル",IsCancel=true,MinWidth=110});
        apply.Click+=(_,_)=>{try{foreach(string key in numbers.Keys)_=Number(key);DialogResult=true;}catch(Exception ex){MessageBox.Show(this,ex.Message,"入力を確認してください");}};
        root.Children.Add(new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    public void Text(string text)=>fields.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,8)});
    public void Number(string key,string title,int initial,int min,int max)
    {
        Text(title);var box=new TextBox{Text=initial.ToString(),Height=32,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,0,8,0)};fields.Children.Add(box);numbers[key]=box;limits[key]=(min,max);
    }
    public int Number(string key){var limit=limits[key];if(!int.TryParse(numbers[key].Text,out int n)||n<limit.Min||n>limit.Max)throw new InvalidOperationException($"{key}: {limit.Min}～{limit.Max}の整数を入力してください。");return n;}
    public void Choice(string key,string title,string[] values,int selected=0){Text(title);var combo=new ComboBox{ItemsSource=values,SelectedIndex=selected,Height=32,Margin=new Thickness(0,0,0,6)};choices[key]=combo;fields.Children.Add(combo);}
    public int Choice(string key)=>choices[key].SelectedIndex;
    public void Check(string key,string title,bool initial=false){var box=new CheckBox{Content=title,IsChecked=initial,Foreground=Foreground,Margin=new Thickness(0,5,0,5)};checks[key]=box;fields.Children.Add(box);}
    public bool Check(string key)=>checks[key].IsChecked==true;
    public void Action(string title,Action action){var button=new Button{Content=title,Margin=new Thickness(0,8,0,4)};fields.Children.Add(button);button.Click+=(_,_)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"操作できません");}};}
}
