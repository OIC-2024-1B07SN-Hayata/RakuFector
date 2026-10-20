namespace ChordDetection.App.Services;

/// <summary>Keeps WPF dialogs out of the ViewModel.</summary>
public interface IFileDialogService
{
    string? OpenFile(string title, string filter);

    string? SaveFile(string title, string filter, string defaultFileName);

    void ShowError(string message);
}
