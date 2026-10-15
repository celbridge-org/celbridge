using System.Threading.Channels;
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
    private Channel<(string ProjectFilePath, ProjectLoadOrigin Origin)> _loads = null!;
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

        // The startup flow can finish on another thread, so each load goes through a channel.
        _loads = Channel.CreateUnbounded<(string ProjectFilePath, ProjectLoadOrigin Origin)>();
        var commandService = Substitute.For<ICommandService>();
        commandService.Execute<ILoadProjectCommand>(default, default!, default)
            .ReturnsForAnyArgs(callInfo =>
            {
                var configure = callInfo.ArgAt<Action<ILoadProjectCommand>?>(0);
                var command = Substitute.For<ILoadProjectCommand>();
                configure?.Invoke(command);
                _loads.Writer.TryWrite((command.ProjectFilePath, command.Origin));

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

        (await ReadLoadAsync()).Should().Be((_launchProjectFile, ProjectLoadOrigin.LaunchFile));
        _loads.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task ALaunchThatNamesNoProject_OpensThePreviousProjectAsThePreviousProject()
    {
        _messengerService.Send(new MainPageLoadedMessage());

        (await ReadLoadAsync()).Should().Be((_previousProjectFile, ProjectLoadOrigin.PreviousProject));
        _loads.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task ALaunchOnAMissingProjectFile_OpensThePreviousProjectAsThePreviousProject()
    {
        _service.OnFilesActivated([Path.Combine(_folderPath, "Missing", "Missing.celbridge")]);

        _messengerService.Send(new MainPageLoadedMessage());

        (await ReadLoadAsync()).Should().Be((_previousProjectFile, ProjectLoadOrigin.PreviousProject));
        _loads.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task AProjectFileSentAfterStartup_OpensAsAnOpenFromARunningWindow()
    {
        // The startup flow opens the previous project only once startup has completed.
        _messengerService.Send(new MainPageLoadedMessage());
        (await ReadLoadAsync()).Should().Be((_previousProjectFile, ProjectLoadOrigin.PreviousProject));

        _service.OnFilesActivated([_launchProjectFile]);

        (await ReadLoadAsync()).Should().Be((_launchProjectFile, ProjectLoadOrigin.RunningWindow));
    }

    private string CreateProjectFile(string name)
    {
        var projectFolderPath = Path.Combine(_folderPath, name);
        Directory.CreateDirectory(projectFolderPath);

        var projectFilePath = Path.Combine(projectFolderPath, $"{name}.celbridge");
        File.WriteAllText(projectFilePath, string.Empty);

        return projectFilePath;
    }

    // The startup flow runs from the message handler without being awaited, so the test waits for each load.
    private async Task<(string ProjectFilePath, ProjectLoadOrigin Origin)> ReadLoadAsync()
    {
        using var timeout = new CancellationTokenSource(LoadTimeoutMilliseconds);
        try
        {
            return await _loads.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("The activation service opened no project.");
            throw;
        }
    }
}
