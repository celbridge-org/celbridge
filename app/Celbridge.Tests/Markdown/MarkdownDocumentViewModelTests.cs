using Celbridge.Core;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Markdown;

/// <summary>
/// Tests for the DocumentViewModel base class text file operations,
/// previously tested through MarkdownDocumentViewModel (now merged into CodeEditorViewModel).
/// Uses a minimal test subclass to exercise the base class behavior.
/// </summary>
[TestFixture]
public class MarkdownDocumentViewModelTests
{
    private IMessengerService _messengerService = null!;
    private TestDocumentViewModel _vm = null!;
    private string _tempFolder = null!;
    private string _tempFilePath = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();

        var services = new ServiceCollection();
        services.AddSingleton(_messengerService);
        services.AddSingleton(Substitute.For<IWorkspaceWrapper>());
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(MarkdownDocumentViewModelTests));
        Directory.CreateDirectory(_tempFolder);

        _tempFilePath = Path.Combine(_tempFolder, "test.md");
        File.WriteAllText(_tempFilePath, string.Empty);

        _vm = new TestDocumentViewModel();
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
    public async Task SaveDocumentContent_WritesContentToFile()
    {
        var content = "# Saved Content";

        var result = await _vm.SaveDocumentContent(content);

        result.IsSuccess.Should().BeTrue();
        var written = await File.ReadAllTextAsync(_tempFilePath);
        written.Should().Be(content);
    }

    [Test]
    public async Task SaveDocumentContent_ClearsUnsavedChanges()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 5.0;

        await _vm.SaveDocumentContent("content");

        _vm.HasUnsavedChanges.Should().BeFalse();
        _vm.SaveTimer.Should().Be(0);
    }

    [Test]
    public async Task SaveDocumentContent_ReturnsFailure_WhenPathIsInvalid()
    {
        var invalidPath = Path.Combine(_tempFolder, "nonexistent_dir", "nested", "file.md");
        _vm.FilePath = invalidPath;

        var result = await _vm.SaveDocumentContent("content");

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
        var savingVm = new TestDocumentViewModel();
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

    /// <summary>
    /// Minimal test subclass that exposes DocumentViewModel base class functionality
    /// for testing text file operations and file-change monitoring.
    /// </summary>
    private sealed class TestDocumentViewModel : DocumentViewModel
    {
        public TestDocumentViewModel()
        {
            EnableFileChangeMonitoring();
        }

        public async Task<Result<string>> LoadDocument()
        {
            return await LoadTextFromFileAsync();
        }

        public async Task<Result> SaveDocumentContent(string text)
        {
            HasUnsavedChanges = false;
            SaveTimer = 0;
            return await SaveTextToFileAsync(text);
        }

        public void OnTextChanged()
        {
            HasUnsavedChanges = true;
            SaveTimer = SaveDelay;
        }

        public void SetIsSavingFile(bool value) => IsSavingFile = value;
    }
}
