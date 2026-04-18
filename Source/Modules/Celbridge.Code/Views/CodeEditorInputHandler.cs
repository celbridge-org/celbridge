using Celbridge.Commands;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.UserInterface;

namespace Celbridge.Code.Views;

/// <summary>
/// Handles IHostInput RPC methods for the code editor.
/// Forwards keyboard shortcuts, scroll position changes, and link activations
/// to the host application.
/// </summary>
internal sealed class CodeEditorInputHandler : IHostInput
{
    private readonly CodeEditorState _state;
    private readonly Action<double> _onScrollPositionChanged;
    private readonly Action<double> _onPreviewScrollChanged;

    public CodeEditorInputHandler(
        CodeEditorState state,
        Action<double> onScrollPositionChanged,
        Action<double> onPreviewScrollChanged)
    {
        _state = state;
        _onScrollPositionChanged = onScrollPositionChanged;
        _onPreviewScrollChanged = onPreviewScrollChanged;
    }

    public void OnKeyboardShortcut(string key, bool ctrlKey, bool shiftKey, bool altKey)
    {
        var keyboardShortcutService = ServiceLocator.AcquireService<IKeyboardShortcutService>();
        keyboardShortcutService.HandleShortcut(key, ctrlKey, shiftKey, altKey);
    }

    public void OnScrollPositionChanged(double scrollPercentage)
    {
        _onScrollPositionChanged(scrollPercentage);
    }

    public void OnPreviewScrollChanged(double scrollPercentage)
    {
        _onPreviewScrollChanged(scrollPercentage);
    }

    public void OnOpenResource(string href)
    {
        if (string.IsNullOrEmpty(_state.FilePath) ||
            string.IsNullOrEmpty(_state.ProjectFolderPath))
        {
            return;
        }

        var documentFolder = Path.GetDirectoryName(_state.FilePath);
        if (string.IsNullOrEmpty(documentFolder))
        {
            return;
        }

        var logger = ServiceLocator.AcquireService<ILogger<CodeEditorInputHandler>>();
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        var fullPath = Path.GetFullPath(Path.Combine(documentFolder, href));

        if (!fullPath.StartsWith(_state.ProjectFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning($"Link path is outside project folder: {href}");
            return;
        }

        var resourcePath = fullPath.Substring(_state.ProjectFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
        var resourceKeyString = resourcePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!ResourceKey.TryCreate(resourceKeyString, out var resourceKey))
        {
            logger.LogWarning($"Invalid resource key derived from link: {resourceKeyString}");
            return;
        }

        commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });
    }

    public void OnOpenExternal(string href)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = href;
        });
    }
}
