using HyperTool.Services;
using HyperTool.WinUI.Helpers;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;

namespace HyperTool.WinUI.Services;

public sealed class UiInteropService : IUiInteropService
{
    public UnsavedConfigPromptResult ShowUnsavedConfigPrompt()
    {
        var result = NativeMessageBox.Show(
            "Es gibt ungespeicherte Einstellungen. Jetzt speichern?",
            "HyperTool",
            NativeMessageBoxButtons.YesNoCancel,
            NativeMessageBoxIcon.Question);

        return result switch
        {
            NativeMessageBoxResult.Yes => UnsavedConfigPromptResult.Yes,
            NativeMessageBoxResult.No => UnsavedConfigPromptResult.No,
            _ => UnsavedConfigPromptResult.Cancel
        };
    }

    public void SetClipboardText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    public string? PickFolderPath(string description)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Auswählen"
        };

        picker.FileTypeFilter.Add("*");

        if (WindowHandleProvider.MainWindowHandle != nint.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandleProvider.MainWindowHandle);
        }

        var folder = picker.PickSingleFolderAsync().AsTask().GetAwaiter().GetResult();
        return folder?.Path;
    }

    public string? PickFilePath(string description, IReadOnlyList<string> fileTypeFilter)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = string.IsNullOrWhiteSpace(description) ? "Auswählen" : description.Trim()
        };

        if (fileTypeFilter is null || fileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }
        else
        {
            foreach (var filter in fileTypeFilter)
            {
                var normalized = (filter ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (!normalized.StartsWith('.'))
                {
                    normalized = "." + normalized;
                }

                picker.FileTypeFilter.Add(normalized);
            }

            if (picker.FileTypeFilter.Count == 0)
            {
                picker.FileTypeFilter.Add("*");
            }
        }

        if (WindowHandleProvider.MainWindowHandle != nint.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandleProvider.MainWindowHandle);
        }

        var file = picker.PickSingleFileAsync().AsTask().GetAwaiter().GetResult();
        return file?.Path;
    }

    public void ShutdownApplication()
    {
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
