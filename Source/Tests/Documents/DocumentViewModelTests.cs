using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
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
    private IFileStorage _fileStorage = null!;
    private IResourceRegistry _resourceRegistry = null!;
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

        // Wire a real FileStorage over a substituted workspace hierarchy
        // whose registry maps the test's resource key to the temp file path. The
        // layer's atomic write + retry semantics are exercised directly against
        // the temp folder.
        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(Result<string>.Ok(_tempFilePath));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _fileStorage = new FileStorage(Substitute.For<ILogger<FileStorage>>(), _messengerService, workspaceWrapper, TestFileSystem.CreateLocal());
        workspaceService.FileStorage.Returns(_fileStorage);

        var services = new ServiceCollection();
        services.AddSingleton(_messengerService);
        services.AddSingleton(workspaceWrapper);
        services.AddSingleton<IFileSystem>(TestFileSystem.CreateLocal());
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _vm = new TestDocumentViewModel(_fileStorage);
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
        // Point the registry at a path that doesn't exist on disk so the
        // gateway-routed read fails. Setting FilePath alone is not enough
        // because the read goes through ResolveResourcePath(FileResource).
        var missingPath = Path.Combine(_tempFolder, "nonexistent.md");
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>())
            .Returns(Result<string>.Ok(missingPath));

        _vm.FilePath = missingPath;

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

        var failingFileSystem = new FileStorage(Substitute.For<ILogger<FileStorage>>(), _messengerService, failingWrapper, TestFileSystem.CreateLocal());

        var failingVm = new TestDocumentViewModel(failingFileSystem)
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
    public void ResourceChanged_TriggersReload_WhenFileChangedExternally()
    {
        // With no prior load/save the hash is null, so any change is treated as external
        var reloadRequested = false;
        _vm.ReloadRequested += (_, _) => reloadRequested = true;

        var message = new ResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        reloadRequested.Should().BeTrue();
    }

    [Test]
    public void OnResourceChanged_ResetsSaveTimer_WhenExternalChangeArrives()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 0.5;

        var message = new ResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        _vm.SaveTimer.Should().Be(0);
    }

    [Test]
    public void OnResourceChanged_ResetsHasUnsavedChanges_WhenExternalChangeArrives()
    {
        _vm.HasUnsavedChanges = true;
        _vm.SaveTimer = 0.5;

        var message = new ResourceChangedMessage(_vm.FileResource);
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
    public async Task Save_RaisesReloadRequested_WhenPostWriteDiskSizeDiffersFromBytesWritten()
    {
        // Simulate an external write that interleaves with our save: the
        // ExternalWriteDocumentViewModel rewrites the file with different-length
        // content immediately after WriteAllBytesAsync but before
        // UpdateFileTrackingInfoAsync runs. The post-write size mismatch flags
        // the interleave and the reload fires. Same-length interleaves slip
        // past this check and rely on the watcher's subsequent event.
        var externalContent = "external content that overrode our save";
        var savingVm = new ExternalWriteDocumentViewModel(_fileStorage, _tempFilePath, externalContent);
        savingVm.FileResource = new ResourceKey("interleave.md");
        savingVm.FilePath = _tempFilePath;

        var reloadRequested = false;
        savingVm.ReloadRequested += (_, _) => reloadRequested = true;

        var saveResult = await savingVm.SaveDocumentContent("our intended content");

        saveResult.IsSuccess.Should().BeTrue();
        reloadRequested.Should().BeTrue();

        savingVm.Cleanup();
    }

    [Test]
    public async Task OnResourceChanged_DoesNotRaiseReload_AfterOwnSaveCompletes()
    {
        // After we save, the cache holds the size + mtime of our own write.
        // A watcher event for that same write (the self-event the gateway's
        // atomic write produces) probes the disk, finds the metadata unchanged
        // from the cache, and returns without raising ReloadRequested. This is
        // the test that proves the Excel-flash regression is gone.
        var saveResult = await _vm.SaveDocumentContent("first save");
        saveResult.IsSuccess.Should().BeTrue();

        var reloadRequested = false;
        _vm.ReloadRequested += (_, _) => reloadRequested = true;

        var message = new ResourceChangedMessage(_vm.FileResource);
        _messengerService.Send(message);

        reloadRequested.Should().BeFalse();
    }

    /// <summary>
    /// Minimal test subclass that exposes DocumentViewModel base class functionality
    /// for testing text file operations and file-change monitoring.
    /// </summary>
    private sealed class TestDocumentViewModel : DocumentViewModel
    {
        private readonly IFileStorage _fileStorage;

        public TestDocumentViewModel(IFileStorage fileStorage)
        {
            _fileStorage = fileStorage;
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

        protected override IFileStorage GetFileSystem() => _fileStorage;
    }

    /// <summary>
    /// Test subclass that simulates an external write interleaving between our
    /// WriteAllBytesAsync call and the post-write tracking refresh. The override
    /// of UpdateFileTrackingInfoAsync runs immediately before the base reads
    /// disk metadata, so by writing different-length content here we make the
    /// cached size differ from the bytes we wrote — which is what the
    /// post-write size-mismatch check looks for.
    /// </summary>
    private sealed class ExternalWriteDocumentViewModel : DocumentViewModel
    {
        private readonly IFileStorage _fileStorage;
        private readonly string _injectedFilePath;
        private readonly string _externalContent;
        private bool _hasInjected;

        public ExternalWriteDocumentViewModel(IFileStorage fileStorage, string filePath, string externalContent)
        {
            _fileStorage = fileStorage;
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

        protected override IFileStorage GetFileSystem() => _fileStorage;

        public override async Task UpdateFileTrackingInfoAsync()
        {
            if (!_hasInjected)
            {
                _hasInjected = true;
                File.WriteAllText(_injectedFilePath, _externalContent);
            }
            await base.UpdateFileTrackingInfoAsync();
        }
    }
}
