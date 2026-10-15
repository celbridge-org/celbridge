using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests which project the activation service opens, and the origin it gives each load. The origin decides what
/// happens when another instance has the project open.
/// </summary>
[TestFixture]
public class AppActivationServiceTests
{
    private const int LoadTimeoutMilliseconds = 5000;

    private string _folderPath = null!;
    private string _launchProjectFile = null!;
    private string _previousProjectFile = null!;
    private IMessengerService _messengerService = null!;
    private ISettingsService _settingsService = null!;
    private List<(string ProjectFilePath, ProjectLoadOrigin Origin)> _loads = null!;
    private AppActivationService _service = null!;

    [SetUp]
    public void Setup()
    {
        _folderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(AppActivationServiceTests),
            Guid.NewGuid().ToString("N"));

        _launchProjectFile = CreateProjectFile("Launch");
        _previousProjectFile = CreateProjectFile("Previous");

        _messengerService = new MessengerService();

        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(_previousProjectFile);

        _loads = new();
        var commandService = Substitute.For<ICommandService>();
        commandService.Execute<ILoadProjectCommand>(default, default!, default)
            .ReturnsForAnyArgs(callInfo =>
            {
                var configure = callInfo.ArgAt<Action<ILoadProjectCommand>?>(0);
                var command = Substitute.For<ILoadProjectCommand>();
                configure?.Invoke(command);
                _loads.Add((command.ProjectFilePath, command.Origin));

                return Result.Ok();
            });

        _service = new AppActivationService(
            new NullLogger<AppActivationService>(),
            _messengerService,
            commandService,
            _settingsService,
            TestFileSystem.CreateLocal());
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
    public async Task ALaunchOnAProjectFile_OpensItAsTheLaunchFile()
    {
        _service.OnFilesActivated([_launchProjectFile]);

        _messengerService.Send(new MainPageLoadedMessage());
        await WaitForLoadAsync();

        _loads.Should().Equal((_launchProjectFile, ProjectLoadOrigin.LaunchFile));
    }

    [Test]
    public async Task ALaunchThatNamesNoProject_OpensThePreviousProjectAsThePreviousProject()
    {
        _messengerService.Send(new MainPageLoadedMessage());
        await WaitForLoadAsync();

        _loads.Should().Equal((_previousProjectFile, ProjectLoadOrigin.PreviousProject));
    }

    [Test]
    public async Task ALaunchOnAMissingProjectFile_OpensThePreviousProjectAsThePreviousProject()
    {
        _service.OnFilesActivated([Path.Combine(_folderPath, "Missing", "Missing.celbridge")]);

        _messengerService.Send(new MainPageLoadedMessage());
        await WaitForLoadAsync();

        _loads.Should().Equal((_previousProjectFile, ProjectLoadOrigin.PreviousProject));
    }

    [Test]
    public async Task AProjectFileSentAfterStartup_OpensAsAnOpenFromARunningWindow()
    {
        _settingsService.Get(SettingCatalog.Project.PreviousProject).Returns(string.Empty);
        _messengerService.Send(new MainPageLoadedMessage());

        _service.OnFilesActivated([_launchProjectFile]);
        await WaitForLoadAsync();

        _loads.Should().Equal((_launchProjectFile, ProjectLoadOrigin.RunningWindow));
    }

    private string CreateProjectFile(string name)
    {
        var projectFolderPath = Path.Combine(_folderPath, name);
        Directory.CreateDirectory(projectFolderPath);

        var projectFilePath = Path.Combine(projectFolderPath, $"{name}.celbridge");
        File.WriteAllText(projectFilePath, string.Empty);

        return projectFilePath;
    }

    // The startup flow runs from the message handler without being awaited, so the test waits for its load.
    private async Task WaitForLoadAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(LoadTimeoutMilliseconds);
        while (_loads.Count == 0)
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("The activation service opened no project.");
            }

            await Task.Delay(20);
        }
    }
}
