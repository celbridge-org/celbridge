using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json.Nodes;

namespace Celbridge.Inspector.ViewModels;

public partial class WebInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<WebInspectorViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;

    [ObservableProperty]
    private string _sourceUrl = string.Empty;

    private bool _supressSaving;

    // Code gen requires a parameterless constructor
    public WebInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public WebInspectorViewModel(
        ILogger<WebInspectorViewModel> logger,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        PropertyChanged += ViewModel_PropertyChanged;
    }

    public IRelayCommand OpenDocumentCommand => new RelayCommand(OpenDocument_Executed);
    private void OpenDocument_Executed()
    {
        // Execute a command to open the web document. Force the document to reload if it is already open.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = Resource;
            command.ForceReload = true;
        });
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var loadResult = LoadURL(webFilePath);
            if (loadResult.IsFailure)
            { 
                _logger.LogError(loadResult, $"Failed to load URL from file: {webFilePath}");
                return;
            }

            _supressSaving = true;
            SourceUrl = loadResult.Value;
            _supressSaving = false;
        }
        else if (e.PropertyName == nameof(SourceUrl) && !_supressSaving)
        {
            var webFilePath = _resourceRegistry.GetResourcePath(Resource);
            var saveResult = SaveURL(webFilePath, SourceUrl);
            if (saveResult.IsFailure)
            {
                _logger.LogError(saveResult, $"Failed to save URL to file: {webFilePath}");
                return;
            }
        }
    }

    private Result<string> LoadURL(string webFilePath)
    {
        if (!File.Exists(webFilePath))
        {
            return Result<string>.Fail($"File not found at path: {webFilePath}");
        }

        try
        {
            var json = File.ReadAllText(webFilePath);

            if (string.IsNullOrEmpty(json))
            {
                // No data populated yet in the .webapp file, default to empty url
                return Result<string>.Ok(string.Empty);
            }

            var jsonObject = JsonNode.Parse(json) as JsonObject;
            if (jsonObject is null)
            {
                return Result<string>.Fail($"Failed to parse JSON file: {webFilePath}");
            }

            var urlToken = jsonObject["sourceUrl"];
            if (urlToken is null)
            {
                return Result<string>.Fail($"'sourceUrl' property not found in JSON file: {webFilePath}");
            }

            string url = urlToken.ToString();

            return Result<string>.Ok(url);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when loading URL from file: {webFilePath}")
                .WithException(ex);
        }
    }

    private Result SaveURL(string webFilePath, string url)
    {
        try
        {
            // Create a new JSON object with just the 'url' property
            var jsonObject = new JsonObject
            {
                ["sourceUrl"] = url
            };

            // Write the new JSON object to the file, overwriting any existing content
            File.WriteAllText(webFilePath, jsonObject.ToJsonString());

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when saving URL to file: {webFilePath}")
                .WithException(ex);
        }
    }
}
