using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.ViewModels;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the New Project dialog's check of a destination folder for a project file. Also tests that the dialog
/// lists a folder once while the user types a name.
/// </summary>
[TestFixture]
public class NewProjectDialogViewModelTests
{
    private const int ValidationTimeoutMilliseconds = 5000;

    private string _folderPath = null!;
    private IProjectService _projectService = null!;
    private NewProjectDialogViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _folderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(NewProjectDialogViewModelTests),
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_folderPath);
        File.WriteAllText(Path.Combine(_folderPath, "Existing.celbridge"), string.Empty);

        var fileSystem = TestFileSystem.CreateLocal();

        _projectService = Substitute.For<IProjectService>();
        _projectService.FindProjectFilesAsync(Arg.Any<string>())
            .Returns(callInfo => ProjectFileHelper.FindProjectFilesAsync(callInfo.Arg<string>(), fileSystem));

        _viewModel = new NewProjectDialogViewModel(
            Substitute.For<ISettingsService>(),
            _projectService,
            Substitute.For<IFilePickerService>(),
            Substitute.For<IProjectTemplateService>(),
            fileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folderPath))
        {
            Directory.Delete(_folderPath, recursive: true);
        }
    }

    [Test]
    public async Task FolderWithAProjectFile_DisablesCreate()
    {
        _viewModel.CreateSubfolder = false;
        _viewModel.DestFolderPath = _folderPath;
        _viewModel.ProjectName = "Acme";

        await WaitForAsync(() => _viewModel.ValidationErrorMessage == "NewProjectDialog_FolderHasProject");

        _viewModel.IsCreateButtonEnabled.Should().BeFalse();
    }

    [Test]
    public async Task NewSubfolder_IgnoresTheProjectFileInTheFolder()
    {
        _viewModel.CreateSubfolder = true;
        _viewModel.DestFolderPath = _folderPath;
        _viewModel.ProjectName = "Acme";

        await WaitForAsync(() => _viewModel.IsCreateButtonEnabled);

        _viewModel.DestProjectFilePath.Should().Be(Path.Combine(_folderPath, "Acme", "Acme.celbridge"));
    }

    [Test]
    public async Task ANewName_DoesNotListTheFolderAgain()
    {
        var emptyFolderPath = Path.Combine(_folderPath, "Empty");
        Directory.CreateDirectory(emptyFolderPath);

        _viewModel.CreateSubfolder = false;
        _viewModel.DestFolderPath = emptyFolderPath;
        _viewModel.ProjectName = "Acme";
        await WaitForAsync(() => _viewModel.DestProjectFilePath == Path.Combine(emptyFolderPath, "Acme.celbridge"));

        _viewModel.ProjectName = "Beta";
        await WaitForAsync(() => _viewModel.DestProjectFilePath == Path.Combine(emptyFolderPath, "Beta.celbridge"));

        await _projectService.Received(1).FindProjectFilesAsync(emptyFolderPath);
    }

    // Validation runs after a debounce, so the test waits for its outcome.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ValidationTimeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The dialog's validation did not reach the expected outcome.");
            }

            await Task.Delay(20);
        }
    }
}
