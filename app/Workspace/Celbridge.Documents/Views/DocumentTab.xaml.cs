using Celbridge.Documents.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Actions available in the document tab context menu.
/// </summary>
public enum DocumentTabMenuAction
{
    Close,
    CloseOthers,
    CloseOthersRight,
    CloseOthersLeft,
    CloseAll,
    CopyResourceKey,
    CopyFilePath,
    SelectFile,
    OpenFileExplorer,
    OpenApplication
}

// I've tried writing this class using a C# Markup class subclassed from TabViewItem, but it didn't work.
// No matter how simple I make the derived class, an exception is thrown when the class is instantiated.
// I've given up for now and am using a XAML file instead.
public partial class DocumentTab : TabViewItem
{
    private readonly IStringLocalizer _stringLocalizer;

    public DocumentTabViewModel ViewModel { get; }

    /// <summary>
    /// Event raised when a context menu action is triggered.
    /// </summary>
    public event Action<DocumentTab, DocumentTabMenuAction>? ContextMenuActionRequested;

    public DocumentTab()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<DocumentTabViewModel>();

        InitializeContextMenuStrings();
    }

    private void InitializeContextMenuStrings()
    {
        CloseMenuItem.Text = _stringLocalizer.GetString("DocumentTab_Close");
        CloseOthersMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseOthers");
        CloseToTheRightMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseRight");
        CloseToTheLeftMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseLeft");
        CloseAllMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CloseAll");
        CopyResourceKeyMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyResourceKey");
        CopyFilePathMenuItem.Text = _stringLocalizer.GetString("DocumentTab_CopyFilePath");
        SelectFileMenuItem.Text = _stringLocalizer.GetString("DocumentTab_SelectFile");
        OpenFileExplorerMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenFileExplorer");
        OpenApplicationMenuItem.Text = _stringLocalizer.GetString("DocumentTab_OpenApplication");
    }

    private void ContextMenu_Close(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.Close);
    }

    private void ContextMenu_CloseOthers(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthers);
    }

    private void ContextMenu_CloseToTheRight(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthersRight);
    }

    private void ContextMenu_CloseToTheLeft(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseOthersLeft);
    }

    private void ContextMenu_CloseAll(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CloseAll);
    }

    private void ContextMenu_SelectFile(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.SelectFile);
    }

    private void ContextMenu_CopyResourceKey(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CopyResourceKey);
    }

    private void ContextMenu_CopyFilePath(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.CopyFilePath);
    }

    private void ContextMenu_OpenFileExplorer(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.OpenFileExplorer);
    }

    private void ContextMenu_OpenApplication(object sender, RoutedEventArgs e)
    {
        ContextMenuActionRequested?.Invoke(this, DocumentTabMenuAction.OpenApplication);
    }
}
