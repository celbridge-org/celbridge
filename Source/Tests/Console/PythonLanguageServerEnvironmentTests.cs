using Celbridge.Console;
using Celbridge.FileSystem.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Python;
using Celbridge.Python.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers the interpreter given to the Python language server. The interpreter is the first Python console's while
/// that console runs, and the celbridge tool environment's at every other time.
/// </summary>
[TestFixture]
public class PythonLanguageServerEnvironmentTests
{
    private static readonly string DataFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PythonLanguageServerEnvironmentTests));
    private static readonly string ConsoleEnvironmentFolder = Path.Combine(DataFolderPath, "builds-v0", ".tmpAbc");

    private IWorkspaceWrapper _workspaceWrapper = null!;
    private CelbridgeWheelFolders _folders = null!;
    private PythonLanguageServerEnvironment _environment = null!;

    [SetUp]
    public void Setup()
    {
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(DataFolderPath);
        appEnvironment.DependenciesFolderPath.Returns(Path.Combine(DataFolderPath, "Dependencies"));

        _folders = new CelbridgeWheelFolders(appEnvironment);

        var pythonConfigService = Substitute.For<IPythonConfigService>();
        var basedpyrightDependency = new BasedpyrightDependency(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            appEnvironment,
            pythonConfigService,
            Substitute.For<ILocalizerService>(),
            Substitute.For<IUvRunner>(),
            _folders,
            new NullLogger<BasedpyrightDependency>());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);

        _environment = new PythonLanguageServerEnvironment(basedpyrightDependency, _folders, _workspaceWrapper);
    }

    [Test]
    public void InterpreterPath_TheFirstPythonConsoleRuns_IsTheConsolesInterpreter()
    {
        SetRunTargets(
            MakeRunTarget("alpha.console", "shell", string.Empty),
            MakeRunTarget("beta.console", "python", ConsoleEnvironmentFolder),
            MakeRunTarget("gamma.console", "python", "/cache/gamma"));

        var interpreterPath = _environment.InterpreterPath;

        var expectedPath = OperatingSystem.IsWindows()
            ? Path.Combine(ConsoleEnvironmentFolder, "Scripts", "python.exe")
            : Path.Combine(ConsoleEnvironmentFolder, "bin", "python");
        interpreterPath.Should().Be(expectedPath);
    }

    [Test]
    public void InterpreterPath_TheFirstPythonConsoleReportedNoEnvironment_IsTheToolEnvironmentsInterpreter()
    {
        SetRunTargets(
            MakeRunTarget("alpha.console", "python", string.Empty),
            MakeRunTarget("beta.console", "python", ConsoleEnvironmentFolder));

        var interpreterPath = _environment.InterpreterPath;

        interpreterPath.Should().Be(_folders.CelbridgeToolInterpreterPath);
    }

    [Test]
    public void InterpreterPath_NoPythonConsoleRuns_IsTheToolEnvironmentsInterpreter()
    {
        SetRunTargets(MakeRunTarget("alpha.console", "shell", string.Empty));

        var interpreterPath = _environment.InterpreterPath;

        interpreterPath.Should().Be(_folders.CelbridgeToolInterpreterPath);
    }

    [Test]
    public void InterpreterPath_NoWorkspace_IsTheToolEnvironmentsInterpreter()
    {
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var interpreterPath = _environment.InterpreterPath;

        interpreterPath.Should().Be(_folders.CelbridgeToolInterpreterPath);
    }

    [Test]
    public void ServerExecutablePath_IsInsideTheDependenciesFolder()
    {
        _environment.ServerExecutablePath.Should().StartWith(Path.Combine(DataFolderPath, "Dependencies"));
    }

    private static ConsoleRunTarget MakeRunTarget(string resourcePath, string sessionTypeId, string environmentFolder)
    {
        return new ConsoleRunTarget(
            Guid.NewGuid(),
            new ResourceKey(resourcePath),
            resourcePath,
            sessionTypeId,
            environmentFolder);
    }

    // The Run menu's targets for a Python file, in the menu's order.
    private void SetRunTargets(params ConsoleRunTarget[] runTargets)
    {
        _workspaceWrapper.WorkspaceService.ConsoleService.Sessions
            .GetRunTargets(".py")
            .Returns(runTargets);
    }
}
