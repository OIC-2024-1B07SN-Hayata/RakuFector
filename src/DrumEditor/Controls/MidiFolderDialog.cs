using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrumEditor.Files;

namespace DrumEditor.Controls;

public sealed class MidiFolderDialog : Window
{
    public string? SelectedPath { get; private set; }
    public MidiFolderDialog(string directory, MidiFolderEntry[] entries, bool exporting, string patternName = "")
    {
        DialogTheme.Apply(this);
        Title = exporting ? "MIDIフォルダーへ書き出し" : "MIDIフォルダーから読み込み";
        Width = 600; Height = exporting ? 320 : 440; MinWidth = 400; MinHeight = exporting ? 320 : 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 26, 31)); Foreground = Brushes.LightGray;
        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = layout;
        layout.Children.Add(new TextBlock { Text = directory, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), MaxHeight = 60, ToolTip = directory });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var accept = new Button { Content = exporting ? "書き出し" : "読み込み", MinWidth = 100, Height = 36, IsDefault = true, IsEnabled = exporting };
        var cancel = new Button { Content = "キャンセル", MinWidth = 100, Height = 36, IsCancel = true };
        actions.Children.Add(accept); actions.Children.Add(cancel);
        if (exporting)
        {
            var field = new StackPanel(); Grid.SetRow(field, 1); layout.Children.Add(field);
            field.Children.Add(new TextBlock { Text = "ファイル名（.mid / .midi）", Margin = new Thickness(0, 0, 0, 8) });
            var name = new TextBox { Text = MidiFolder.SuggestedName(patternName), HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 16, Height = 36 };
            field.Children.Add(name);
            field.Children.Add(new TextBlock { Text = "楽器音量はMIDIには保存されません。JSON保存で保持します。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,8,0,0) });
            accept.Click += (_, _) =>
            {
                try { SelectedPath = MidiFolder.ExportPath(directory, name.Text); DialogResult = true; }
                catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException) { MessageBox.Show(this, ex.Message, "ファイル名を確認してください"); }
            };
            Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        }
        else
        {
            var list = new ListBox { ItemsSource = entries, DisplayMemberPath = nameof(MidiFolderEntry.Name), Background = Background, Foreground = Foreground, FontSize = 16 };
            Grid.SetRow(list, 1); layout.Children.Add(list);
            list.SelectionChanged += (_, _) => accept.IsEnabled = list.SelectedItem is MidiFolderEntry;
            void Select() { if (list.SelectedItem is MidiFolderEntry item) { SelectedPath = item.FullPath; DialogResult = true; } }
            accept.Click += (_, _) => Select();
            list.MouseDoubleClick += (_, e) => { if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem) Select(); };
            Loaded += (_, _) => list.Focus();
        }
    }
}
