using System.Text;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Explorer;
using Celbridge.Utilities;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Commands;

/// <summary>
/// Derives a .webview resource name from a page URL.
/// </summary>
public static class WebViewDocumentNaming
{
    // Used when a page host sanitises down to nothing, so the new document still gets a name.
    private const string DefaultPageResourceName = "page";

    /// <summary>
    /// Returns the resource name a page URL suggests, so scratch.mit.edu becomes
    /// scratch_mit_edu.webview. Every character a resource name cannot carry becomes an underscore.
    /// </summary>
    public static string GetResourceName(string url)
    {
        var host = string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }

        var builder = new StringBuilder(host.Length);
        foreach (var character in host)
        {
            bool isAllowed = char.IsLetterOrDigit(character) ||
                             character == '-' ||
                             character == '_';
            builder.Append(isAllowed ? character : '_');
        }

        var name = builder.ToString().Trim('_');
        if (string.IsNullOrEmpty(name))
        {
            name = DefaultPageResourceName;
        }

        return $"{name}{ExplorerConstants.WebViewExtension}";
    }
}

public class CreateWebViewDialogCommand : CommandBase, ICreateWebViewDialogCommand
{
    private const string DialogTitleKey = "WebView_NewDocumentDialog_Title";
    private const string DocumentNameKey = "WebView_NewDocumentDialog_DocumentName";
    private const string CreateButtonKey = "DialogButton_Create";

    // Bounds the disambiguation loop that looks for an unused default name.
    private const int MaxUniqueNameAttempts = 100;

    public override CommandFlags CommandFlags => CommandFlags.None;

    public string SourceUrl { get; set; } = string.Empty;
    public ResourceKey DestFolderResource { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public CreateWebViewDialogCommand(
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Failed to show the new web view document dialog because workspace is not loaded");
        }

        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            return Result.Fail("Failed to create a web view document because no page URL was supplied");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(DestFolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve destination folder: '{DestFolderResource}'")
                .WithErrors(getResult);
        }

        var parentFolder = getResult.Value as IFolderResource;
        if (parentFolder is null)
        {
            return Result.Fail($"Parent folder resource key '{DestFolderResource}' does not reference a folder resource.");
        }

        var getDefaultResult = await FindDefaultDocumentNameAsync();
        if (getDefaultResult.IsFailure)
        {
            return Result.Fail()
                .WithErrors(getDefaultResult);
        }
        var defaultDocumentName = getDefaultResult.Value;

        var validator = _serviceProvider.GetRequiredService<IResourceNameValidator>();
        validator.ParentFolder = parentFolder;
        validator.ValidateAsFolder = false;

        var selectionRange = ResourceNameHelper.GetNameSelectionRange(defaultDocumentName);

        var titleString = _stringLocalizer.GetString(DialogTitleKey);
        var nameString = _stringLocalizer.GetString(DocumentNameKey);

        var showResult = await _dialogService.ShowInputTextDialogAsync(
            titleString,
            nameString,
            defaultDocumentName,
            selectionRange,
            validator,
            CreateButtonKey);

        if (showResult.IsFailure)
        {
            // The user cancelled the dialog.
            return Result.Ok();
        }

        var documentName = showResult.Value;
        var newResource = DestFolderResource.Combine(documentName);

        // Creation and content are two commands: the create command seeds a new file from the file
        // template rather than from supplied content, so the Home URL is written afterwards. Both are
        // queued rather than awaited, because awaiting a command from inside a running command deadlocks
        // the queue. Serial execution runs them in the order they are queued.
        _commandService.Execute<ICreateResourceCommand>(command =>
        {
            command.ResourceType = ResourceType.File;
            command.DestResource = newResource;
            command.OpenAfterCreating = false;
        });

        var content = new WebViewFileContent(SourceUrl);

        _commandService.Execute<IWriteFileCommand>(command =>
        {
            command.FileResource = newResource;
            command.Content = content.ToToml();
        });

        return Result.Ok();
    }

    // Appends " (N)" before the extension until the suggested name is unused, so the dialog opens on a
    // name the user can accept rather than on a validation error.
    private async Task<Result<string>> FindDefaultDocumentNameAsync()
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var resourceName = WebViewDocumentNaming.GetResourceName(SourceUrl);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(resourceName);
        var extension = Path.GetExtension(resourceName);

        var candidateName = resourceName;
        for (int count = 1; count <= MaxUniqueNameAttempts; count++)
        {
            var candidateKey = DestFolderResource.Combine(candidateName);

            var infoResult = await resourceFileSystem.GetInfoAsync(candidateKey);
            if (infoResult.IsFailure)
            {
                return Result<string>.Fail($"Failed to check whether resource '{candidateKey}' exists")
                    .WithErrors(infoResult);
            }

            if (infoResult.Value.Kind == StorageItemKind.NotFound)
            {
                return candidateName;
            }

            candidateName = $"{nameWithoutExtension} ({count}){extension}";
        }

        return Result<string>.Fail($"Failed to find an unused name for '{resourceName}' in '{DestFolderResource}'");
    }
}
