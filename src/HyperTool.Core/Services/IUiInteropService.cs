namespace HyperTool.Services;

public enum UnsavedConfigPromptResult
{
    Yes,
    No,
    Cancel
}

public interface IUiInteropService
{
    UnsavedConfigPromptResult ShowUnsavedConfigPrompt();

    void ShowInfoMessage(string title, string message);

    void SetClipboardText(string text);

    string? PickFolderPath(string description);

    string? PickFilePath(string description, IReadOnlyList<string> fileTypeFilter);

    void ShutdownApplication();
}
