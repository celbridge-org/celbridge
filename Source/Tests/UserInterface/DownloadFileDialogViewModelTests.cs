using Celbridge.Projects;
using Celbridge.UserInterface.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers what the Download File dialog shows: the site offering the file, the download folder, and the title and
/// button texts of its two pages. The project folder shows as the project's name.
/// </summary>
[TestFixture]
public class DownloadFileDialogViewModelTests
{
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

        _viewModel = new DownloadFileDialogViewModel(stringLocalizer, projectService);
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
        _viewModel.Initialize("report.pdf", "https://example.com/files/report.pdf", ResourceKey.Empty);

        _viewModel.FileName.Should().Be("report.pdf");
        _viewModel.SourceText.Should().Be("DownloadFileDialog_Source:example.com");
        _viewModel.SourceVisibility.Should().Be(Visibility.Visible);

        _viewModel.DownloadFolder.Should().Be(ResourceKey.Empty);
        _viewModel.DownloadFolderText.Should().Be("ResourcePickerDialog_ProjectFolder:Acme");

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
        _viewModel.Initialize("hello.txt", "data:text/plain;base64,SGVsbG8=", ResourceKey.Empty);

        _viewModel.SourceText.Should().BeEmpty();
        _viewModel.SourceVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public void AFolderInTheProject_ShowsAsItsPath()
    {
        _viewModel.Initialize("report.pdf", "https://example.com/report.pdf", new ResourceKey("downloads"));

        _viewModel.DownloadFolderText.Should().Be("downloads");

        var incomingFolder = new ResourceKey("downloads/incoming");
        _viewModel.SetDownloadFolder(incomingFolder);

        _viewModel.DownloadFolder.Should().Be(incomingFolder);
        _viewModel.DownloadFolderText.Should().Be("downloads/incoming");
    }

    [Test]
    public void TheSecondPage_ChoosesTheFolder_AndTheFirstPageReturns()
    {
        _viewModel.Initialize("report.pdf", "https://example.com/report.pdf", ResourceKey.Empty);

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
}
