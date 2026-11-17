using Celbridge.Automation;
using Celbridge.Dependencies;
using Celbridge.FileSystem.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Python;
using Celbridge.Python.Services;
using Celbridge.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers the celbridge wheel's install in a temporary data folder. A fake runner replaces uv and returns set
/// outcomes. A failed online tool install runs again offline, and the install writes the marker only once the tool
/// is installed.
/// </summary>
[TestFixture]
public class CelbridgeWheelDependencyTests
{
    private const string WheelFileName = "celbridge-0.1.0-py3-none-any.whl";

    private string _rootFolderPath = null!;
    private string _assetsFolderPath = null!;
    private LocalFileSystem _fileSystem = null!;
    private IAppEnvironment _appEnvironment = null!;
    private UvDependency _uvDependency = null!;
    private CelbridgeWheelFolders _folders = null!;
    private IServiceProvider? _previousServiceProvider;

    [SetUp]
    public void SetUp()
    {
        _rootFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(CelbridgeWheelDependencyTests), Guid.NewGuid().ToString("N"));
        _assetsFolderPath = Path.Combine(_rootFolderPath, "Assets", "Python");
        var dataFolderPath = Path.Combine(_rootFolderPath, "Data");
        Directory.CreateDirectory(_assetsFolderPath);
        File.WriteAllText(Path.Combine(_assetsFolderPath, WheelFileName), "a wheel");

        _fileSystem = new LocalFileSystem(new NullLogger<LocalFileSystem>());

        // Hashing the bundled wheel acquires the file system through the ServiceLocator.
        var services = new ServiceCollection();
        services.AddSingleton<ILocalFileSystem>(_fileSystem);
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _appEnvironment = Substitute.For<IAppEnvironment>();
        _appEnvironment.LocalApplicationDataFolderPath.Returns(dataFolderPath);
        _appEnvironment.DependenciesFolderPath.Returns(Path.Combine(dataFolderPath, "Dependencies"));
        _appEnvironment.GetBundledAssetPath("Celbridge.Python", "Assets/Python").Returns(_assetsFolderPath);
        _appEnvironment.GetEnvironmentInfo().Returns(new EnvironmentInfo("1.0.0", "macOS", "Debug"));

        _uvDependency = new UvDependency(
            _fileSystem,
            _appEnvironment,
            Substitute.For<IDependencyDownloader>(),
            Substitute.For<ILocalizerService>(),
            new NullLogger<UvDependency>());

        _folders = new CelbridgeWheelFolders(_appEnvironment);
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }

        Directory.Delete(_rootFolderPath, recursive: true);
    }

    [Test]
    public void ReadPyvenvHome_ReturnsTheInterpreterFolder()
    {
        var configText =
            "home = C:\\Store\\cpython-3.13-windows-x86_64-none\r\n" +
            "implementation = CPython\r\n" +
            "version_info = 3.13.12\r\n";

        var home = CelbridgeWheelDependency.ReadPyvenvHome(configText);

        home.Should().Be(@"C:\Store\cpython-3.13-windows-x86_64-none");
    }

    [Test]
    public void ReadPyvenvHome_WithoutAHomeKey_ReturnsNull()
    {
        var configText =
            "implementation = CPython\n" +
            "extends-environment = /store/cpython-3.13-macos-aarch64-none/bin\n";

        var home = CelbridgeWheelDependency.ReadPyvenvHome(configText);

        home.Should().BeNull();
    }

    [Test]
    public async Task Install_ASuccessfulOnlineRun_InstallsTheToolOnceIntoTheSupportFolders()
    {
        CreateUvExecutable();
        var uvRunner = new FakeUvRunner(Succeeded());
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue();
        uvRunner.Invocations.Should().ContainSingle();
        var invocation = uvRunner.Invocations[0];
        invocation.Arguments.Should().Contain("--no-config");
        invocation.Arguments.Should().ContainInOrder("--python", "3.13", "--managed-python");
        invocation.Arguments.Should().NotContain("--offline");
        invocation.Arguments[^1].Should().Be(Path.Combine(_folders.PythonFolderPath, WheelFileName));
        invocation.Variables["UV_TOOL_DIR"].Should().Be(_folders.UvToolsFolderPath);
        invocation.Variables["UV_TOOL_BIN_DIR"].Should().Be(_folders.UvToolBinFolderPath);
        invocation.Variables["UV_PYTHON_INSTALL_DIR"].Should().Be(_folders.UvPythonInstallFolderPath);
        invocation.Variables["UV_CACHE_DIR"].Should().Be(_folders.UvCacheFolderPath);
        File.Exists(_folders.InstalledVersionFilePath).Should().BeTrue();
    }

    [Test]
    public async Task Install_AFailedOnlineRun_RunsAgainOfflineAndWritesTheMarker()
    {
        CreateUvExecutable();
        var uvRunner = new FakeUvRunner(
            Failed("The package index could not be reached"),
            Succeeded());
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsSuccess.Should().BeTrue();
        uvRunner.Invocations.Should().HaveCount(2);
        uvRunner.Invocations[1].Arguments.Should().Equal([.. uvRunner.Invocations[0].Arguments, "--offline"]);
        File.Exists(_folders.InstalledVersionFilePath).Should().BeTrue();
    }

    [Test]
    public async Task Install_BothRunsFailing_ReportsBothErrorsAndWritesNoMarker()
    {
        CreateUvExecutable();
        var uvRunner = new FakeUvRunner(
            Failed("The package index could not be reached"),
            Failed("The cache has no ipython"));
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("The package index could not be reached");
        result.MessageChain.Should().Contain("The cache has no ipython");
        File.Exists(_folders.InstalledVersionFilePath).Should().BeFalse();
    }

    [Test]
    public async Task Install_WithoutUv_FailsAndKeepsTheInstalledFolder()
    {
        var markerPath = _folders.InstalledVersionFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "a marker from an earlier release");
        var uvRunner = new FakeUvRunner();
        var dependency = CreateDependency(uvRunner);

        var result = await dependency.InstallAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("uv");
        uvRunner.Invocations.Should().BeEmpty();
        File.ReadAllText(markerPath).Should().Be("a marker from an earlier release");
    }

    private void CreateUvExecutable()
    {
        Directory.CreateDirectory(_uvDependency.FolderPath);
        File.WriteAllText(_uvDependency.ExecutablePath, "uv");
    }

    private CelbridgeWheelDependency CreateDependency(IUvRunner uvRunner)
    {
        var pythonConfigService = Substitute.For<IPythonConfigService>();
        pythonConfigService.DefaultPythonVersion.Returns("3.13");

        return new CelbridgeWheelDependency(
            _fileSystem,
            new NullLogger<CelbridgeWheelDependency>(),
            _appEnvironment,
            pythonConfigService,
            Substitute.For<IAutomationService>(),
            Substitute.For<ILocalizerService>(),
            _uvDependency,
            uvRunner,
            _folders);
    }

    private static UvRunOutcome Succeeded() => new(0, string.Empty, string.Empty);

    private static UvRunOutcome Failed(string standardError) => new(2, string.Empty, standardError);

    private sealed class FakeUvRunner : IUvRunner
    {
        private readonly Queue<UvRunOutcome> _outcomes;

        public FakeUvRunner(params UvRunOutcome[] outcomes)
        {
            _outcomes = new Queue<UvRunOutcome>(outcomes);
        }

        public List<UvInvocation> Invocations { get; } = new();

        public Task<Result<UvRunOutcome>> RunAsync(UvInvocation invocation)
        {
            Invocations.Add(invocation);

            return Task.FromResult(Result<UvRunOutcome>.Ok(_outcomes.Dequeue()));
        }
    }
}
