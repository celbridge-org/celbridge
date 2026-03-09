using Celbridge.Markdown.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;

namespace Celbridge.Tests.Markdown;

[TestFixture]
public class MarkdownDocumentViewModelTests
{
    private IMessengerService _messengerService = null!;
    private MarkdownDocumentViewModel _vm = null!;
    private string _tempFolder = null!;
    private string _tempFilePath = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(MarkdownDocumentViewModelTests));
        Directory.CreateDirectory(_tempFolder);

        _tempFilePath = Path.Combine(_tempFolder, "test.md");
        File.WriteAllText(_tempFilePath, string.Empty);

        _vm = new MarkdownDocumentViewModel(_messengerService);
        _vm.FileResource = new ResourceKey("test.md");
        _vm.FilePath = _tempFilePath;
    }

    [TearDown]
    public void TearDown()
    {
        _vm.Cleanup();
        _messengerService.UnregisterAll(this);

        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task LoadDocument_ReturnsContent_WhenFileExists()
    {
        var content = "# Hello World";
        await File.WriteAllTextAsync(_tempFilePath, content);

        var result = await _vm.LoadDocument();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(content);
    }

    [Test]
    public async Task LoadDocument_ReturnsFailure_WhenFileIsMissing()
    {
        _vm.FilePath = Path.Combine(_tempFolder, "nonexistent.md");

        var result = await _vm.LoadDocument();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task SaveDocument_WritesContentToFile()
    {
        var content = "# Saved Content";

        var result = await _vm.SaveDocument(content);

        result.IsSuccess.Should().BeTrue();
        var written = await File.ReadAllTextAsync(_tempFilePath);
        written.Should().Be(content);
    }

    [Test]
    public async Task SaveDocument_ClearsUnsavedChanges_AndSendsCompletionMessage()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 5.0;

        var messageReceived = false;
        var receivedResource = ResourceKey.Empty;
        _messengerService.Register<DocumentSaveCompletedMessage>(this, (_, m) =>
        {
            messageReceived = true;
            receivedResource = m.DocumentResource;
        });

        await _vm.SaveDocument("content");

        _vm.HasUnsavedChanges.Should().BeFalse();
        _vm.SaveTimer.Should().Be(0);
        messageReceived.Should().BeTrue();
        receivedResource.Should().Be(_vm.FileResource);
    }

    [Test]
    public async Task SaveDocument_ReturnsFailure_WhenPathIsInvalid()
    {
        var invalidPath = Path.Combine(_tempFolder, "nonexistent_dir", "nested", "file.md");
        _vm.FilePath = invalidPath;

        var result = await _vm.SaveDocument("content");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void OnTextChanged_SetsUnsavedChanges_AndResetsSaveTimer()
    {
        _vm.OnTextChanged();

        _vm.HasUnsavedChanges.Should().BeTrue();
        _vm.SaveTimer.Should().Be(1.0);
    }

    [Test]
    public void ViewMode_RaisesViewModeChangedEvent()
    {
        MarkdownViewMode? receivedMode = null;
        _vm.ViewModeChanged += (_, mode) => receivedMode = mode;

        _vm.ViewMode = MarkdownViewMode.Source;

        receivedMode.Should().Be(MarkdownViewMode.Source);
    }

    [Test]
    public void MonitoredResourceChanged_TriggersReload_WhenFileChangedExternally()
    {
        // With no prior load/save the hash is null, so any change is treated as external
        var reloadRequested = false;
        _vm.ReloadRequested += (_, _) => reloadRequested = true;

        var message = new MonitoredResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        reloadRequested.Should().BeTrue();
    }

    [Test]
    public void MonitoredResourceChanged_SkipsReload_WhenIsSavingFile()
    {
        var savingVm = new TestableMarkdownDocumentViewModel(_messengerService);
        savingVm.FileResource = new ResourceKey("saving.md");
        savingVm.FilePath = _tempFilePath;
        savingVm.SetIsSavingFile(true);

        var reloadRequested = false;
        savingVm.ReloadRequested += (_, _) => reloadRequested = true;

        var message = new MonitoredResourceChangedMessage(savingVm.FileResource);
        _messengerService.Send(message);

        reloadRequested.Should().BeFalse();

        savingVm.Cleanup();
    }

    private sealed class TestableMarkdownDocumentViewModel : MarkdownDocumentViewModel
    {
        public TestableMarkdownDocumentViewModel(IMessengerService messengerService)
            : base(messengerService)
        {
        }

        public void SetIsSavingFile(bool value) => IsSavingFile = value;
    }
}
