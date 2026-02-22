using System.Text.Json;
using System.Text.Json.Nodes;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class NoteDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;

    // Delay before saving the document after the most recent change
    private const double SaveDelay = 1.0; // Seconds

    [ObservableProperty]
    private double _saveTimer;

    // Event to notify the view that the document should be reloaded
    public event EventHandler? ReloadRequested;

    public NoteDocumentViewModel(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    private void OnMonitoredResourceChangedMessage(object recipient, MonitoredResourceChangedMessage message)
    {
        if (message.Resource == FileResource)
        {
            if (IsFileChangedExternally())
            {
                ReloadRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        if (message.DocumentResource == FileResource)
        {
            UpdateFileTrackingInfo();
        }
    }

    public void OnDataChanged()
    {
        HasUnsavedChanges = true;
        SaveTimer = SaveDelay;
    }

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        if (!HasUnsavedChanges)
        {
            return Result<bool>.Fail($"Document does not have unsaved changes: {FileResource}");
        }

        if (SaveTimer > 0)
        {
            SaveTimer -= deltaTime;
            if (SaveTimer <= 0)
            {
                SaveTimer = 0;
                return Result<bool>.Ok(true);
            }
        }

        return Result<bool>.Ok(false);
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            await EnsureNoteFileAsync();

            UpdateFileTrackingInfo();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }
    }

    public async Task<string> LoadNoteDocJson()
    {
        var fileContent = await File.ReadAllTextAsync(FilePath);

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return GetDefaultDocJson();
        }

        try
        {
            var envelope = JsonNode.Parse(fileContent);
            var doc = envelope?["doc"];
            if (doc is null)
            {
                return GetDefaultDocJson();
            }

            return doc.ToJsonString();
        }
        catch
        {
            return GetDefaultDocJson();
        }
    }

    public async Task<Result> SaveDocument()
    {
        HasUnsavedChanges = false;
        SaveTimer = 0;

        // The actual saving is handled in NoteDocumentView
        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> SaveNoteToFile(string docJsonString)
    {
        try
        {
            var docNode = JsonNode.Parse(docJsonString);

            // Try to read existing envelope to preserve created timestamp
            string? existingCreated = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    var existing = await File.ReadAllTextAsync(FilePath);
                    if (!string.IsNullOrWhiteSpace(existing))
                    {
                        var existingEnvelope = JsonNode.Parse(existing);
                        existingCreated = existingEnvelope?["meta"]?["created"]?.GetValue<string>();
                    }
                }
                catch
                {
                    // Ignore errors reading existing file
                }
            }

            var now = DateTime.UtcNow.ToString("o");
            var created = existingCreated ?? now;
            var title = ExtractTitle(docNode);

            var envelope = new JsonObject
            {
                ["format"] = "note",
                ["version"] = 1,
                ["doc"] = docNode,
                ["meta"] = new JsonObject
                {
                    ["title"] = title,
                    ["created"] = created,
                    ["modified"] = now
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = envelope.ToJsonString(options);

            await File.WriteAllTextAsync(FilePath, json);

            var message = new DocumentSaveCompletedMessage(FileResource);
            _messengerService.Send(message);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save Note file: '{FilePath}'")
                .WithException(ex);
        }
    }

    public void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }

    private async Task EnsureNoteFileAsync()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(FilePath);
        if (!string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        // File is empty, write default envelope
        var now = DateTime.UtcNow.ToString("o");

        var envelope = new JsonObject
        {
            ["format"] = "note",
            ["version"] = 1,
            ["doc"] = JsonNode.Parse(GetDefaultDocJson()),
            ["meta"] = new JsonObject
            {
                ["title"] = "Untitled",
                ["created"] = now,
                ["modified"] = now
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(FilePath, envelope.ToJsonString(options));
    }

    private static string GetDefaultDocJson()
    {
        return """{"type":"doc","content":[{"type":"paragraph"}]}""";
    }

    private static string ExtractTitle(JsonNode? docNode)
    {
        try
        {
            var content = docNode?["content"]?.AsArray();
            if (content is null)
            {
                return "Untitled";
            }

            foreach (var node in content)
            {
                if (node?["type"]?.GetValue<string>() == "heading")
                {
                    var textContent = node["content"]?.AsArray();
                    if (textContent != null && textContent.Count > 0)
                    {
                        var text = textContent[0]?["text"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors extracting title
        }

        return "Untitled";
    }
}
