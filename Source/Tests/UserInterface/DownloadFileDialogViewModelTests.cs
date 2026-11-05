using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.UserInterface.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers what the Download File dialog shows: the site offering the file, the download folder, and the title and
/// button texts of its two pages. The project folder shows as the project's name. The remembered choice starts
/// selected. Other location opens the system save dialog on the file's name.
/// </summary>
[TestFixture]
public class DownloadFileDialogViewModelTests
{
    private IFilePickerService _filePickerService = null!;
    private DownloadFileDialogViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("Acme");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        // A formatted string comes back as the string's name and first argument, so a test can read the argument.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            var arguments = callInfo.Arg<object[]>();
            return new LocalizedString(name, $"{name}:{arguments[0]}");
        });

        _filePickerService = Substitute.For<IFilePickerService>();
        _filePickerService.PickSaveFileAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<PickerStartFolder>(),
                Arg.Any<string>())
            .Returns(Task.FromResult(Result<string>.Ok("/Users/acme/Downloads/report.pdf")));

        _viewModel = new DownloadFileDialogViewModel(stringLocalizer, projectService, _filePickerService);
    }

    [TestCase("https://example.com/files/report.pdf", "example.com")]
    [TestCase("blob:https://example.com/550e8400-e29b-41d4-a716-446655440000", "example.com")]
    [TestCase("data:text/plain;base64,SGVsbG8=", "")]
    public void TheSiteName_IsTheHostOfTheAddress(string sourceUrl, string expectedSiteName)
    {
        DownloadFileDialogViewModel.GetSiteName(sourceUrl).Should().Be(expectedSiteName);
    }

    [Test]
    public void TheFirstPage_ShowsTheFileTheSiteAndTheProjectFolder()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/files/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        _viewModel.FileName.Should().Be("report.pdf");
        _viewModel.SourceText.Should().Be("DownloadFileDialog_Source:example.com");
        _viewModel.SourceVisibility.Should().Be(Visibility.Visible);

        _viewModel.DownloadFolder.Should().Be(ResourceKey.Empty);
        _viewModel.DownloadFolderText.Should().Be("ResourcePickerDialog_ProjectFolder:Acme");
        _viewModel.IsInProjectSelected.Should().BeTrue();
        _viewModel.IsOtherLocationSelected.Should().BeFalse();

        _viewModel.IsChoosingFolder.Should().BeFalse();
        _viewModel.TitleText.Should().Be("DownloadFileDialog_Title");
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_Save");
        _viewModel.SecondaryButtonText.Should().Be("DialogButton_Cancel");
        _viewModel.FilePageVisibility.Should().Be(Visibility.Visible);
        _viewModel.FolderPageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public void AnAddressWithNoHost_HidesTheSite()
    {
        _viewModel.Initialize(
            "hello.txt",
            "data:text/plain;base64,SGVsbG8=",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        _viewModel.SourceText.Should().BeEmpty();
        _viewModel.SourceVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public void AFolderInTheProject_ShowsAsItsPath()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/report.pdf",
            new ResourceKey("downloads"),
            DownloadFileChoice.InProject);

        _viewModel.DownloadFolderText.Should().Be("downloads");

        var incomingFolder = new ResourceKey("downloads/incoming");
        _viewModel.SetDownloadFolder(incomingFolder);

        _viewModel.DownloadFolder.Should().Be(incomingFolder);
        _viewModel.DownloadFolderText.Should().Be("downloads/incoming");
    }

    [Test]
    public void TheSecondPage_ChoosesTheFolder_AndTheFirstPageReturns()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        _viewModel.ShowFolderPage();

        _viewModel.IsChoosingFolder.Should().BeTrue();
        _viewModel.TitleText.Should().Be("DownloadFileDialog_SelectFolderTitle");
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_Select");
        _viewModel.SecondaryButtonText.Should().Be("DialogButton_Back");
        _viewModel.FilePageVisibility.Should().Be(Visibility.Collapsed);
        _viewModel.FolderPageVisibility.Should().Be(Visibility.Visible);

        _viewModel.ShowFilePage();

        _viewModel.IsChoosingFolder.Should().BeFalse();
        _viewModel.TitleText.Should().Be("DownloadFileDialog_Title");
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_Save");
        _viewModel.SecondaryButtonText.Should().Be("DialogButton_Cancel");
        _viewModel.FilePageVisibility.Should().Be(Visibility.Visible);
        _viewModel.FolderPageVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public void ARememberedChoiceOfOtherLocation_SelectsOtherLocation()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/report.pdf",
            new ResourceKey("downloads"),
            DownloadFileChoice.OtherLocation);

        _viewModel.RememberedChoice.Should().Be(DownloadFileChoice.OtherLocation);
        _viewModel.IsOtherLocationSelected.Should().BeTrue();
        _viewModel.IsInProjectSelected.Should().BeFalse();

        // The download folder stays ready for the In project option.
        _viewModel.DownloadFolderText.Should().Be("downloads");
    }

    [Test]
    public async Task OtherLocation_AfterAProjectFolderSave_OpensTheSaveDialogOnTheDownloadsFolder()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        var pickResult = await _viewModel.PickOtherLocationAsync();

        pickResult.IsSuccess.Should().BeTrue();
        pickResult.Value.Should().Be("/Users/acme/Downloads/report.pdf");

        await _filePickerService.Received(1).PickSaveFileAsync(
            "report.pdf",
            "DownloadFileDialog_FileType:PDF",
            Arg.Is<IEnumerable<string>>(extensions => extensions.SequenceEqual(new[] { ".pdf" })),
            PickerStartFolder.Downloads,
            "DownloadFile");
    }

    [Test]
    public async Task OtherLocation_AfterAnOtherLocationSave_OpensTheSaveDialogOnTheFolderLastUsed()
    {
        _viewModel.Initialize(
            "report.pdf",
            "https://example.com/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.OtherLocation);

        await _viewModel.PickOtherLocationAsync();

        await _filePickerService.Received(1).PickSaveFileAsync(
            "report.pdf",
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            PickerStartFolder.LastUsed,
            "DownloadFile");
    }

    [Test]
    public async Task OtherLocation_ForANameWithNoExtension_OffersNoFileType()
    {
        _viewModel.Initialize(
            "README",
            "https://example.com/README",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        await _viewModel.PickOtherLocationAsync();

        await _filePickerService.Received(1).PickSaveFileAsync(
            "README",
            Arg.Any<string>(),
            Arg.Is<IEnumerable<string>>(extensions => !extensions.Any()),
            PickerStartFolder.Downloads,
            "DownloadFile");
    }
}
