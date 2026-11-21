using Celbridge.FileSystem.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Python;
using Celbridge.Python.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers basedpyright as a dependency. A fake runner replaces uv. The tests cover the check for the pinned
/// version's language server, and the uv tool install into the version's folder. They also cover the offline run
/// after a failed install, and the deletion of other versions' folders.
/// </summary>
[TestFixture]
public class BasedpyrightDependencyTests
{
    private const string Version = "1.40.2";

    private static readonly string ServerExecutableName = OperatingSystem.IsWindows()
        ? "basedpyright-langserver.exe"
        : "basedpyright-langserver";

    private string _dataFolderPath = null!;
    private string _versionsFolderPath = null!;
    private string _interpreterFolderPath = null!;
    private IAppEnvironment _appEnvironment = null!;
    private CelbridgeWheelFolders _folders = null!;

    [SetUp]
    public void Setup()
    {
        _dataFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(BasedpyrightDependencyTests), Guid.NewGuid().ToString("N"));
        var dependenciesFolderPath = Path.Combine(_dataFolderPath, "Dependencies");
        _versionsFolderPath = Path.Combine(dependenciesFolderPath, "LanguageServers", "basedpyright");
        Directory.CreateDirectory(_versionsFolderPath);

        _appEnvironment = Substitute.For<IAppEnvironment>();
        _appEnvironment.LocalApplicationDataFolderPath.Returns(_dataFolderPath);
        _appEnvironment.DependenciesFolderPath.Returns(dependenciesFolderPath);

        _folders = new CelbridgeWheelFolders(_appEnvironment);

        _interpreterFolderPath = Path.Combine(_dataFolderPath, "Interpreter");
        Directory.CreateDirectory(_interpreterFolderPath);
        File.WriteAllText(Path.Combine(_interpreterFolderPath, "python.exe"), "an interpreter");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataFolderPath))
        {
            Directory.Delete(_dataFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task IsInstalled_ThePinnedVersionsServerPresent_ReturnsTrue()
    {
        CreateServerExecutable(Version);
        var dependency = CreateDependency(new FakeUvRunner());

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeTrue();
    }

    [Test]
    public async Task IsInstalled_AVersionFolderWithoutTheServer_ReturnsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_versionsFolderPath, Version, "bin"));
        var dependency = CreateDependency(new FakeUvRunner());

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task IsInstalled_TheInterpreterBehindTheServerGone_ReturnsFalse()
    {
        CreateServerExecutable(Version);
        Directory.Delete(_interpreterFolderPath, recursive: true);
        var dependency = CreateDependency(new FakeUvRunner());

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task IsInstalled_OnlyAnotherVersionsServerPresent_ReturnsFalse()
    {
        CreateServerExecutable("1.39.0");
        var dependency = CreateDependency(new FakeUvRunner());

        var isInstalled = await dependency.IsInstalledAsync();

        isInstalled.Should().BeFalse();
    }

    [Test]
    public async Task Install_RunsTheUvToolInstallIntoTheVersionsFolder()
    {
        var uvRunner = new FakeUvRunner(() => CreateServerExecutable(Version));
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        uvRunner.Invocations.Should().HaveCount(1);

        var invocation = uvRunner.Invocations[0];
        invocation.Arguments.Should().Equal(
            "tool", "install", "--force", "--no-config", "--python", "3.13", "--managed-python", "basedpyright==1.40.2");

        var versionFolderPath = Path.Combine(_versionsFolderPath, Version);
        invocation.Variables["UV_TOOL_DIR"].Should().Be(versionFolderPath);
        invocation.Variables["UV_TOOL_BIN_DIR"].Should().Be(Path.Combine(versionFolderPath, "bin"));
        invocation.Variables["UV_PYTHON_INSTALL_DIR"].Should().Be(_folders.UvPythonInstallFolderPath);
        invocation.Variables["UV_CACHE_DIR"].Should().Be(_folders.UvCacheFolderPath);
    }

    [Test]
    public async Task Install_AFailedOnlineInstall_RunsAgainOffline()
    {
        var uvRunner = new FakeUvRunner(() => CreateServerExecutable(Version));
        uvRunner.FailedRunCount = 1;
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        uvRunner.Invocations.Should().HaveCount(2);
        uvRunner.Invocations[0].Arguments.Should().NotContain("--offline");
        uvRunner.Invocations[1].Arguments.Should().Contain("--offline");
    }

    [Test]
    public async Task Install_FailedOnlineAndOffline_Fails()
    {
        var uvRunner = new FakeUvRunner();
        uvRunner.FailedRunCount = 2;
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
        uvRunner.Invocations.Should().HaveCount(2);
    }

    [Test]
    public async Task Install_AFailedInstallWhileAnotherCopyInstalledTheServer_Succeeds()
    {
        var uvRunner = new FakeUvRunner();
        uvRunner.FailedRunCount = 2;
        uvRunner.OnFailure = () => CreateServerExecutable(Version);
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
    }

    [Test]
    public async Task Install_AnInstallThatLeavesNoServer_Fails()
    {
        var uvRunner = new FakeUvRunner();
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Install_AServerThatAnotherCopyInstalledFirst_RunsNothing()
    {
        CreateServerExecutable(Version);
        var uvRunner = new FakeUvRunner();
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        uvRunner.Invocations.Should().BeEmpty();
    }

    [Test]
    public async Task Install_ANewVersion_DeletesTheOtherVersionsFolders()
    {
        CreateServerExecutable("1.39.0");
        Directory.CreateDirectory(Path.Combine(_versionsFolderPath, "notes"));
        var uvRunner = new FakeUvRunner(() => CreateServerExecutable(Version));
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue(result.MessageChain);
        Directory.Exists(Path.Combine(_versionsFolderPath, "1.39.0")).Should().BeFalse();
        Directory.Exists(Path.Combine(_versionsFolderPath, "notes")).Should().BeTrue("only a folder named by a version is deleted");
        Directory.Exists(Path.Combine(_versionsFolderPath, Version)).Should().BeTrue();
    }

    [Test]
    public void PinnedVersion_IsAVersionNumber()
    {
        System.Version.TryParse(BasedpyrightDependency.PinnedVersion, out _).Should().BeTrue();
    }

    // Creates what an install leaves: the server's launcher, and the tool environment's pyvenv.cfg, which names
    // the folder of the shared interpreter.
    private void CreateServerExecutable(string version)
    {
        var versionFolderPath = Path.Combine(_versionsFolderPath, version);
        var binFolderPath = Path.Combine(versionFolderPath, "bin");
        Directory.CreateDirectory(binFolderPath);
        File.WriteAllText(Path.Combine(binFolderPath, ServerExecutableName), "a server");

        var environmentFolderPath = Path.Combine(versionFolderPath, "basedpyright");
        Directory.CreateDirectory(environmentFolderPath);
        File.WriteAllText(Path.Combine(environmentFolderPath, "pyvenv.cfg"), $"home = {_interpreterFolderPath}\n");
    }

    private BasedpyrightDependency CreateDependency(IUvRunner uvRunner)
    {
        var pythonConfigService = Substitute.For<IPythonConfigService>();
        pythonConfigService.DefaultPythonVersion.Returns("3.13");

        return new BasedpyrightDependency(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            _appEnvironment,
            pythonConfigService,
            Substitute.For<ILocalizerService>(),
            uvRunner,
            _folders,
            new NullLogger<BasedpyrightDependency>(),
            Version);
    }

    // Replaces uv. The first runs fail, up to FailedRunCount. Each later run succeeds. Before a run succeeds, the
    // runner calls the action, which does uv's work on disk. OnFailure runs during each failed run.
    private sealed class FakeUvRunner : IUvRunner
    {
        private readonly Action? _onSuccess;

        public FakeUvRunner(Action? onSuccess = null)
        {
            _onSuccess = onSuccess;
        }

        public int FailedRunCount { get; set; }

        public Action? OnFailure { get; set; }

        public List<UvInvocation> Invocations { get; } = new();

        public Task<Result<UvRunOutcome>> RunAsync(UvInvocation invocation)
        {
            Invocations.Add(invocation);

            if (Invocations.Count <= FailedRunCount)
            {
                OnFailure?.Invoke();

                var failedOutcome = new UvRunOutcome(2, string.Empty, "no network");
                return Task.FromResult(Result<UvRunOutcome>.Ok(failedOutcome));
            }

            _onSuccess?.Invoke();

            var outcome = new UvRunOutcome(0, string.Empty, string.Empty);
            return Task.FromResult(Result<UvRunOutcome>.Ok(outcome));
        }
    }
}
