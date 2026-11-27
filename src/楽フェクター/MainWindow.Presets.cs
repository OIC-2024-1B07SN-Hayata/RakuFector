using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FXForge.Data;

namespace FXForge;

public partial class MainWindow
{
    private readonly RakufekutaDatabase _presetDatabase = new();

    // 読込・保存に成功したプリセットだけを更新対象にする。
    private PresetListItem? _loadedPreset;

    private bool _presetBusy;
    private bool _updatingPresetList;
    private bool _presetEventsAttached;
    private bool _presetWindowClosed;
    private bool _reopeningPresetList;

    private void PresetShell_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_presetEventsAttached)
        {
            return;
        }

        _presetEventsAttached = true;

        Closed += (_, _) =>
        {
            _presetWindowClosed = true;
        };

        var menu = new ContextMenu();

        var saveAs = new MenuItem
        {
            Header = "別名で新規保存…"
        };

        saveAs.Click += async (_, _) =>
        {
            await SavePresetAsync(forceNew: true);
        };

        menu.Items.Add(saveAs);

        SavePresetButton.ContextMenu = menu;

        SetPresetBusy(false);
        UpdatePresetHints();
    }

    private void SetPresetBusy(bool busy)
    {
        _presetBusy = busy;

        SavePresetButton.IsEnabled = !busy;
        PresetComboBox.IsEnabled = !busy;
    }

    private void UpdatePresetHints()
    {
        SavePresetButton.Content = "保存";

        SavePresetButton.ToolTip = _loadedPreset is null
            ? "名前を付けて新規保存します。"
            : $"「{_loadedPreset.Name}」を更新します。\n" +
              "右クリックで別名の新規保存ができます。";

        PresetComboBox.ToolTip =
            (_loadedPreset is null
                ? "更新対象は未選択です。"
                : $"更新対象: {_loadedPreset.Name}" +
                  $"（ID: {_loadedPreset.Id}）") +
            "\n" +
            _presetDatabase.DatabasePath;
    }

    private async void SavePreset_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SavePresetAsync(forceNew: false);
    }

    private async Task SavePresetAsync(bool forceNew)
    {
        if (_presetBusy ||
            _checkingClose ||
            _presetWindowClosed)
        {
            return;
        }

        SetPresetBusy(true);

        try
        {
            PresetListItem? target =
                forceNew ? null : _loadedPreset;

            bool updating = target is not null;

            string name;

            if (target is null)
            {
                string previousName = "";

                while (true)
                {
                    string? enteredName =
                        AskPresetName(previousName);

                    if (enteredName is null)
                    {
                        return;
                    }

                    previousName = enteredName;

                    // 通常の同名操作は例外にせず、
                    // 保存する前に一覧で確認する。
                    var items =
                        await _presetDatabase.ListAsync();

                    if (_presetWindowClosed || _checkingClose)
                    {
                        return;
                    }

                    bool duplicate = items.Any(
                        item => string.Equals(
                            item.Name,
                            enteredName,
                            StringComparison.Ordinal));

                    if (duplicate)
                    {
                        MessageBox.Show(
                            this,
                            $"「{enteredName}」は既に登録されています。\n\n" +
                            "別の名前を入力してください。\n" +
                            "既存のプリセットを更新する場合は、" +
                            "一覧から選択してから「保存」を押してください。",
                            "同じ名前では新規保存できません",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        // 名前を残した状態で入力画面を開き直す。
                        continue;
                    }

                    name = enteredName;
                    break;
                }
            }
            else
            {
                name = target.Name;

                // 別の操作や別プロセスで削除されていた場合も、
                // 通常の確認メッセージとして扱う。
                var items =
                    await _presetDatabase.ListAsync();

                if (_presetWindowClosed || _checkingClose)
                {
                    return;
                }

                bool exists = items.Any(
                    item => item.Id == target.Id);

                if (!exists)
                {
                    MessageBox.Show(
                        this,
                        "更新対象のプリセットが見つかりません。\n\n" +
                        "プリセット一覧を開いて選び直してください。\n" +
                        "現在の音作りを新規保存する場合は、" +
                        "保存ボタンを右クリックして" +
                        "「別名で新規保存…」を選択してください。",
                        "更新できません",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            if (_presetWindowClosed || _checkingClose)
            {
                return;
            }

            // UIスレッドで現在の音作り設定を取得する。
            var snapshot = _tone.CapturePreset();

            if (target is null)
            {
                long id =
                    await _presetDatabase.SaveNewAsync(
                        name,
                        snapshot);

                target = new PresetListItem(id, name);
            }
            else
            {
                await _presetDatabase.UpdateAsync(
                    target.Id,
                    snapshot);
            }

            if (_presetWindowClosed)
            {
                return;
            }

            // 保存成功後にだけ更新対象を切り替える。
            _loadedPreset = target;

            UpdatePresetHints();

            try
            {
                await RefreshPresetListAsync();
            }
            catch (Exception ex)
            {
                if (!_presetWindowClosed)
                {
                    MessageBox.Show(
                        this,
                        "保存は完了しましたが、" +
                        "一覧の更新に失敗しました。\n" +
                        "プルダウンを開いて再取得してください。\n\n" +
                        ex.Message,
                        "保存完了・一覧更新エラー",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    $"「{target.Name}」を" +
                    $"{(updating ? "更新" : "新規保存")}しました。",
                    "プリセット保存",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            // DB接続失敗、書込失敗、不正な保存データなど。
            // 保存失敗時は_loadedPresetを変更しない。
            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    "プリセットを保存できませんでした。\n\n" +
                    ex.Message,
                    "保存エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            if (!_presetWindowClosed)
            {
                SetPresetBusy(false);
            }
        }
    }

    private async void PresetComboBox_DropDownOpened(
        object? sender,
        EventArgs e)
    {
        if (_reopeningPresetList ||
            _presetBusy ||
            _checkingClose ||
            _presetWindowClosed)
        {
            return;
        }

        PresetComboBox.IsDropDownOpen = false;

        SetPresetBusy(true);

        try
        {
            // 表示上の選択だけを解除する。
            // 成功済みの更新対象は維持する。
            await RefreshPresetListAsync(
                clearSelection: true);

            if (_presetWindowClosed || _checkingClose)
            {
                return;
            }

            if (PresetComboBox.Items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "保存済みのプリセットはありません。",
                    "プリセット一覧",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            SetPresetBusy(false);

            _reopeningPresetList = true;

            try
            {
                PresetComboBox.IsDropDownOpen = true;
            }
            finally
            {
                _reopeningPresetList = false;
            }
        }
        catch (Exception ex)
        {
            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    "プリセット一覧を取得できませんでした。\n\n" +
                    ex.Message,
                    "一覧取得エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            if (!_presetWindowClosed)
            {
                SetPresetBusy(false);
            }
        }
    }

    private async Task RefreshPresetListAsync(
        bool clearSelection = false)
    {
        var items =
            await _presetDatabase.ListAsync();

        if (_presetWindowClosed)
        {
            return;
        }

        _updatingPresetList = true;

        try
        {
            PresetComboBox.ItemsSource = items;

            PresetComboBox.SelectedItem = clearSelection
                ? null
                : items.FirstOrDefault(
                    item => item.Id == _loadedPreset?.Id);
        }
        finally
        {
            _updatingPresetList = false;
        }
    }

    private async void PresetComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingPresetList ||
            _presetBusy ||
            _checkingClose ||
            _presetWindowClosed ||
            PresetComboBox.SelectedItem is not PresetListItem item)
        {
            return;
        }

        PresetComboBox.IsDropDownOpen = false;

        SetPresetBusy(true);

        bool applied = false;

        try
        {
            var preset =
                await _presetDatabase.LoadAsync(item.Id);

            if (_presetWindowClosed || _checkingClose)
            {
                return;
            }

            _tone.ApplySavedPreset(preset);

            // DB読込と画面反映の両方が成功した後に更新する。
            _loadedPreset = item;
            applied = true;

            UpdatePresetHints();
        }
        catch (Exception ex)
        {
            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    "プリセットを読み込めませんでした。\n\n" +
                    ex.Message,
                    "読込エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            if (!_presetWindowClosed)
            {
                if (!applied)
                {
                    _updatingPresetList = true;

                    try
                    {
                        // 読込に失敗したプリセットを
                        // 更新対象として扱わない。
                        PresetComboBox.SelectedItem =
                            PresetComboBox.Items
                                .OfType<PresetListItem>()
                                .FirstOrDefault(
                                    value =>
                                        value.Id == _loadedPreset?.Id);
                    }
                    finally
                    {
                        _updatingPresetList = false;
                    }
                }

                SetPresetBusy(false);
            }
        }
    }

    private string? AskPresetName(
        string initialName = "")
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(20)
        };

        panel.Children.Add(new TextBlock
        {
            Text =
                "プリセット名（1～80文字・同名は新規登録できません）",
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var nameBox = new TextBox
        {
            Text = initialName,
            MaxLength = 80,
            MinHeight = 30
        };

        panel.Children.Add(nameBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var save = new Button
        {
            Content = "新規保存",
            IsDefault = true,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            MinWidth = 90
        };

        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Owner = this,
            Title = "プリセットを新規保存",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Content = panel,
            ShowInTaskbar = false
        };

        save.Click += (_, _) =>
        {
            string enteredName = nameBox.Text.Trim();

            // 入力不備は例外にせず、その場で通知する。
            if (enteredName.Length == 0)
            {
                MessageBox.Show(
                    dialog,
                    "プリセット名を入力してください。\n" +
                    "空白だけの名前は使用できません。",
                    "名前の確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                nameBox.Focus();
                nameBox.SelectAll();

                return;
            }

            if (enteredName.Length > 80)
            {
                MessageBox.Show(
                    dialog,
                    "プリセット名は80文字以内にしてください。",
                    "名前の確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                nameBox.Focus();
                nameBox.SelectAll();

                return;
            }

            dialog.DialogResult = true;
        };

        dialog.Loaded += (_, _) =>
        {
            Keyboard.Focus(nameBox);

            if (nameBox.Text.Length > 0)
            {
                nameBox.SelectAll();
            }
        };

        return dialog.ShowDialog() == true
            ? nameBox.Text.Trim()
            : null;
    }
}