using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the DocumentViewModel base class text file operations.
/// Uses a minimal test subclass to exercise the base class behavior.
/// </summary>
[TestFixture]
public class DocumentViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IResourceFileWriter _fileWriter = null!;
    private TestDocumentViewModel _vm = null!;
    private string _tempFolder = null!;
    private string _tempFilePath = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DocumentViewModelTests));
        Directory.CreateDirectory(_tempFolder);

        _tempFilePath = Path.Combine(_tempFolder, "test.md");
        File.WriteAllText(_tempFilePath, string.Empty);

        // Wire a real ResourceFileWriter over a substituted workspace hierarchy
        // whose registry maps the test's resource key to the temp file path. The
        // writer's atomic write + retry semantics are exercised directly against
        // the temp folder.
        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(Result<string>.Ok(_tempFilePath));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _fileWriter = new ResourceFileWriter(Substitute.For<ILogger<ResourceFileWriter>>(), workspaceWrapper);

        var services = new ServiceCollection();
        services.AddSingleton(_messengerService);
        services.AddSingleton(workspaceWrapper);
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _vm = new TestDocumentViewModel(_fileWriter);
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
    public async Task SaveDocumentContent_ReturnsFailure_WhenWriterFails()
    {
        var failingRegistry = Substitute.For<IResourceRegistry>();
        failingRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var failingResourceService = Substitute.For<IResourceService>();
        failingResourceService.Registry.Returns(failingRegistry);

        var failingWorkspaceService = Substitute.For<IWorkspaceService>();
        failingWorkspaceService.ResourceService.Returns(failingResourceService);

        var failingWrapper = Substitute.For<IWorkspaceWrapper>();
        failingWrapper.WorkspaceService.Returns(failingWorkspaceService);

        var failingWriter = new ResourceFileWriter(Substitute.For<ILogger<ResourceFileWriter>>(), failingWrapper);

        var failingVm = new TestDocumentViewModel(failingWriter)
        {
            FileResource = new ResourceKey("test.md"),
            FilePath = _tempFilePath
        };

        var result = await failingVm.SaveDocumentContent("content");

        result.IsFailure.Should().BeTrue();

        failingVm.Cleanup();
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
    public void OnMonitoredResourceChanged_ResetsSaveTimer_WhenExternalChangeArrives()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 0.5;

        var message = new MonitoredResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        _vm.SaveTimer.Should().Be(0);
    }

    [Test]
    public void OnMonitoredResourceChanged_ResetsHasUnsavedChanges_WhenExternalChangeArrives()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 0.5;

        var message = new MonitoredResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        _vm.HasUnsavedChanges.Should().BeFalse();
    }

    [Test]
    public async Task Save_RaisesReloadRequested_WhenDiskChangedBeforeSave()
    {
        // Pre-write detection: if disk content has drifted since our last
        // tracked save, an external writer ran before us. The save aborts
        // (disk wins) and a reload is requested so the buffer realigns.
        var initialContent = "initial content";
        await File.WriteAllTextAsync(_tempFilePath, initialContent);

        // Establish baseline tracking by loading the file.
        var loadResult = await _vm.LoadDocument();
        loadResult.IsSuccess.Should().BeTrue();

        // External writer changes disk content.
        var externalContent = "external override";
        await File.WriteAllTextAsync(_tempFilePath, externalContent);

        var reloadRequested = false;
        _vm.ReloadRequested += (_, _) => reloadRequested = true;

        var saveResult = await _vm.SaveDocumentContent("our intended content");

        saveResult.IsSuccess.Should().BeTrue();
        reloadRequested.Should().BeTrue();

        // Disk should still hold the external content — our save was aborted.
        var diskContent = await File.ReadAllTextAsync(_tempFilePath);
        diskContent.Should().Be(externalContent);
    }

    [Test]
    public async Task Save_RaisesReloadRequested_WhenPostWriteDiskHashDiffersFromIntendedHash()
    {
        // Simulate an external write that interleaves with our save: the
        // ExternalWriteDocumentViewModel rewrites the file with different content
        // immediately after we call WriteAllBytesAsync but before
        // UpdateFileTrackingInfo runs.
        var externalContent = "external content that overrode our save";
        var savingVm = new ExternalWriteDocumentViewModel(_fileWriter, _tempFilePath, externalContent);
        savingVm.FileResource = new ResourceKey("interleave.md");
        savingVm.FilePath = _tempFilePath;

        var reloadRequested = false;
        savingVm.ReloadRequested += (_, _) => reloadRequested = true;

        var saveResult = await savingVm.SaveDocumentContent("our intended content");

        saveResult.IsSuccess.Should().BeTrue();
        reloadRequested.Should().BeTrue();

        savingVm.Cleanup();
    }

    /// <summary>
    /// Minimal test subclass that exposes DocumentViewModel base class functionality
    /// for testing text file operations and file-change monitoring.
    /// </summary>
    private sealed class TestDocumentViewModel : DocumentViewModel
    {
        private readonly IResourceFileWriter _writer;

        public TestDocumentViewModel(IResourceFileWriter writer)
        {
            _writer = writer;
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

        protected override IResourceFileWriter GetFileWriter() => _writer;
    }

    /// <summary>
    /// Test subclass that simulates an external write interleaving between our
    /// WriteAllBytesAsync call and the post-write disk hash read. The override
    /// of UpdateFileTrackingInfo runs immediately before the base reads the disk
    /// hash, so by writing different content here we make _lastSavedFileHash
    /// reflect external content while our intendedHash reflects ours.
    /// </summary>
    private sealed class ExternalWriteDocumentViewModel : DocumentViewModel
    {
        private readonly IResourceFileWriter _writer;
        private readonly string _injectedFilePath;
        private readonly string _externalContent;
        private bool _hasInjected;

        public ExternalWriteDocumentViewModel(IResourceFileWriter writer, string filePath, string externalContent)
        {
            _writer = writer;
            _injectedFilePath = filePath;
            _externalContent = externalContent;
            EnableFileChangeMonitoring();
        }

        public Task<Result> SaveDocumentContent(string text)
        {
            HasUnsavedChanges = false;
            SaveTimer = 0;
            return SaveTextToFileAsync(text);
        }

        protected override IResourceFileWriter GetFileWriter() => _writer;

        protected override void UpdateFileTrackingInfo()
        {
            if (!_hasInjected)
            {
                _hasInjected = true;
                File.WriteAllText(_injectedFilePath, _externalContent);
            }
            base.UpdateFileTrackingInfo();
        }
    }
}
