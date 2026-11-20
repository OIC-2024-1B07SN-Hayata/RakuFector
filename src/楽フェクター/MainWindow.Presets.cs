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

    // プルダウンの選択表示とは別に、
    // 読込・保存に成功したプリセットを更新対象として保持する。
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
            return;

        _presetEventsAttached = true;

        Closed += (_, _) =>
        {
            _presetWindowClosed = true;
        };

        // レイアウトを変更せず、保存ボタンの右クリックに
        // 別名で新規保存する操作を追加する。
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

            string? name = target?.Name;

            if (target is null)
            {
                name = AskPresetName();

                if (name is null)
                    return;
            }

            var snapshot = _tone.CapturePreset();

            bool updating = target is not null;

            if (target is null)
            {
                long id = await _presetDatabase.SaveNewAsync(
                    name!,
                    snapshot);

                target = new PresetListItem(id, name!);
            }
            else
            {
                // 選択済みプリセットは同じIDの設定を更新。
                await _presetDatabase.UpdateAsync(
                    target.Id,
                    snapshot);
            }

            if (_presetWindowClosed)
                return;

            // DB保存に成功してから更新対象を変更する。
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
                        "保存は完了しましたが、一覧の更新に失敗しました。\n" +
                        "プルダウンを開いて再取得してください。\n\n" +
                        ex.Message,
                        "保存完了・一覧更新エラー");
                }

                return;
            }

            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    $"「{target.Name}」を" +
                    $"{(updating ? "更新" : "新規保存")}しました。",
                    "プリセット保存");
            }
        }
        catch (Exception ex)
        {
            if (!_presetWindowClosed)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "保存できません");
            }
        }
        finally
        {
            if (!_presetWindowClosed)
                SetPresetBusy(false);
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
            // 表示上の選択だけ解除する。
            // 更新対象の_loadedPresetは解除しない。
            await RefreshPresetListAsync(clearSelection: true);

            if (_presetWindowClosed || _checkingClose)
                return;

            if (PresetComboBox.Items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "保存済みのプリセットはありません。",
                    "プリセット一覧");

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
                    ex.Message,
                    "一覧を取得できません");
            }
        }
        finally
        {
            if (!_presetWindowClosed)
                SetPresetBusy(false);
        }
    }

    private async Task RefreshPresetListAsync(
        bool clearSelection = false)
    {
        var items = await _presetDatabase.ListAsync();

        if (_presetWindowClosed)
            return;

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
            var preset = await _presetDatabase.LoadAsync(item.Id);

            if (_presetWindowClosed || _checkingClose)
                return;

            _tone.ApplySavedPreset(preset);

            // 読込と画面反映の両方が成功してから
            // 保存ボタンの更新対象を切り替える。
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
                    ex.Message,
                    "読み込めません");
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
                        // 失敗した選択を更新対象として扱わない。
                        PresetComboBox.SelectedItem =
                            PresetComboBox.Items
                                .OfType<PresetListItem>()
                                .FirstOrDefault(
                                    value => value.Id == _loadedPreset?.Id);
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

    private string? AskPresetName()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(20)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "プリセット名（1～80文字・同名は新規登録できません）",
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var nameBox = new TextBox
        {
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
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            ShowInTaskbar = false
        };

        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show(
                    dialog,
                    "プリセット名を入力してください。",
                    "名前の確認");

                nameBox.Focus();
                return;
            }

            dialog.DialogResult = true;
        };

        dialog.Loaded += (_, _) =>
        {
            Keyboard.Focus(nameBox);
        };

        return dialog.ShowDialog() == true
            ? nameBox.Text.Trim()
            : null;
    }
}