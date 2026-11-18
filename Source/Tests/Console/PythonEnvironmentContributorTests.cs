using Celbridge.Console;
using Celbridge.Python.Services;
using Celbridge.Settings;

namespace Celbridge.Tests.Console;

[TestFixture]
public class PythonEnvironmentContributorTests
{
    private static readonly string UvFolder = OperatingSystem.IsWindows() ? @"C:\Celbridge\uv" : "/celbridge/uv";

    private IPythonLaunchService _launchService = null!;
    private IFeatureFlags _featureFlags = null!;
    private PythonEnvironmentContributor _contributor = null!;

    [SetUp]
    public void Setup()
    {
        _launchService = Substitute.For<IPythonLaunchService>();
        _launchService.BuildConsoleEnvironmentAsync().Returns(new Dictionary<string, string>
        {
            ["UV_CACHE_DIR"] = "celbridge-cache",
            ["CELBRIDGE_IPYTHON_DIR"] = "celbridge-ipython",
        });
        _launchService.GetConsolePathFolders().Returns(new[] { UvFolder });
        _launchService.BuildConsolePath(Arg.Any<string>()).Returns(callInfo => $"{UvFolder}{Path.PathSeparator}{callInfo.Arg<string>()}");

        _featureFlags = Substitute.For<IFeatureFlags>();

        _contributor = new PythonEnvironmentContributor(
            _launchService,
            _featureFlags,
            Substitute.For<ILogger<PythonEnvironmentContributor>>());
    }

    [Test]
    public async Task PythonFeatureOn_AddsCelbridgesUv_AndRemovesTheInheritedSettings()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(true);
        var inherited = InheritedEnvironment();
        var environment = new Dictionary<string, string>
        {
            ["UV_CACHE_DIR"] = "console-cache",
            ["PATH"] = "base-path",
        };

        _contributor.FilterInheritedEnvironment(inherited);
        await _contributor.ContributeAsync(MakeContext(), environment);

        inherited.Should().NotContainKey("VIRTUAL_ENV");
        inherited.Should().NotContainKey("UV_PYTHON");
        environment["UV_CACHE_DIR"].Should().Be("console-cache", "the console's own value wins");
        environment["CELBRIDGE_IPYTHON_DIR"].Should().Be("celbridge-ipython");
        environment["PATH"].Should().StartWith(UvFolder);
        environment[ConsoleEnvironmentVariables.PathFolders].Should().Be(UvFolder);
        environment[ConsoleEnvironmentVariables.PythonFeature].Should().Be("1", "the start-up then guards Celbridge's uv");
    }

    [Test]
    public async Task PythonFeatureOff_LeavesTheConsoleWithTheUsersOwnSettings()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);
        var inherited = InheritedEnvironment();
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = "base-path",
        };

        _contributor.FilterInheritedEnvironment(inherited);
        await _contributor.ContributeAsync(MakeContext(), environment);

        inherited.Should().BeEquivalentTo(InheritedEnvironment());
        environment.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["PATH"] = "base-path",
        });
        await _launchService.DidNotReceive().BuildConsoleEnvironmentAsync();
    }

    private static Dictionary<string, string> InheritedEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["HOME"] = "home",
            ["VIRTUAL_ENV"] = "user-venv",
            ["UV_PYTHON"] = "user-python",
        };
    }

    private static ConsoleSessionContext MakeContext()
    {
        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            string.Empty,
            new Dictionary<string, string>(),
            string.Empty,
            string.Empty,
            new Dictionary<string, object?>());
    }
}
