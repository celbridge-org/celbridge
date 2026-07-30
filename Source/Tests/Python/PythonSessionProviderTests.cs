using Celbridge.Console;
using Celbridge.Logging;
using Celbridge.Messaging.Services;
using Celbridge.Python;
using Celbridge.Python.Services;

namespace Celbridge.Tests.Python;

[TestFixture]
public class PythonSessionProviderTests
{
    private const string BundledDefaultVersion = "3.13";
    private const string LaunchFingerprint = "fingerprint-1";

    private static string ProjectRoot =>
        OperatingSystem.IsWindows() ? @"C:\Projects\Demo" : "/projects/demo";

    private static string ProjectPythonFolder =>
        Path.Combine(ProjectRoot, ".celbridge", "python");

    private IPythonLaunchService _launchService = null!;
    private MessengerService _messengerService = null!;
    private PythonSessionProvider _provider = null!;
    private PythonLaunchRequest? _capturedRequest;

    [SetUp]
    public void Setup()
    {
        var pythonConfigService = Substitute.For<IPythonConfigService>();
        pythonConfigService.DefaultPythonVersion.Returns(BundledDefaultVersion);

        _capturedRequest = null;
        _launchService = Substitute.For<IPythonLaunchService>();
        _launchService.BuildStartupAsync(Arg.Do<PythonLaunchRequest>(request => _capturedRequest = request))
            .Returns(Result<PythonStartupResult>.Ok(new PythonStartupResult(
                "celbridge-py",
                new Dictionary<string, string>
                {
                    ["CELBRIDGE_PYTHON_VERSION"] = "3.13",
                },
                LaunchFingerprint,
                ProjectPythonFolder)));

        _messengerService = new MessengerService();

        _provider = new PythonSessionProvider(
            pythonConfigService,
            _launchService,
            _messengerService,
            Substitute.For<ILogger<PythonSessionProvider>>());
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    private static ConsoleSessionContext MakeContext(
        string? runtimeVersion = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? startupScript = null)
    {
        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "python",
            string.Empty,
            Array.Empty<string>(),
            string.Empty,
            environment ?? new Dictionary<string, string>(),
            ProjectRoot,
            RuntimeVersion: runtimeVersion,
            StartupScript: startupScript);
    }

    [Test]
    public async Task BuildStartupInvocation_ConsoleVersion_WinsOverBundledDefault()
    {
        var result = await _provider.BuildStartupInvocationAsync(MakeContext(runtimeVersion: "3.11"));

        result.IsFailure.Should().BeFalse();
        _capturedRequest!.PythonVersion.Should().Be("3.11");
    }

    [Test]
    public async Task BuildStartupInvocation_BlankVersion_FallsBackToBundledDefault()
    {
        var result = await _provider.BuildStartupInvocationAsync(MakeContext(runtimeVersion: "  "));

        result.IsFailure.Should().BeFalse();
        _capturedRequest!.PythonVersion.Should().Be(BundledDefaultVersion);
    }

    [Test]
    public async Task BuildStartupInvocation_ReturnsTheBareCommandWithLaunchDefaultEnvironment()
    {
        var result = await _provider.BuildStartupInvocationAsync(MakeContext());

        result.IsFailure.Should().BeFalse();
        var command = result.Value;
        command.Executable.Should().Be("celbridge-py");
        command.Arguments.Should().BeEmpty();
        command.Environment.Should().ContainKey("CELBRIDGE_PYTHON_VERSION").WhoseValue.Should().Be("3.13");
    }

    [Test]
    public async Task BuildStartupInvocation_StartupScript_IsHandledByTheProvider()
    {
        // The REPL discards type-ahead as it starts, so the script must ride the environment into
        // IPython rather than being typed at the prompt by the host.
        var result = await _provider.BuildStartupInvocationAsync(MakeContext(startupScript: "%run \"hello.py\""));

        result.IsFailure.Should().BeFalse();
        var invocation = result.Value;
        invocation.HandlesStartupScript.Should().BeTrue();
        invocation.Environment.Should().ContainKey("CELBRIDGE_PYTHON_STARTUP")
            .WhoseValue.Should().Be("%run \"hello.py\"");
    }

    [Test]
    public async Task BuildStartupInvocation_NoStartupScript_LeavesItToTheHost()
    {
        var result = await _provider.BuildStartupInvocationAsync(MakeContext(startupScript: "   "));

        result.IsFailure.Should().BeFalse();
        var invocation = result.Value;
        invocation.HandlesStartupScript.Should().BeFalse();
        invocation.Environment.Should().NotContainKey("CELBRIDGE_PYTHON_STARTUP");
    }

    [Test]
    public async Task Fingerprint_SavedWhenTheSessionConnects()
    {
        var sessionToken = Guid.NewGuid();
        var environment = new Dictionary<string, string>
        {
            [ConsoleEnvironmentVariables.SessionToken] = sessionToken.ToString(),
        };

        var result = await _provider.BuildStartupInvocationAsync(MakeContext(environment: environment));
        result.IsFailure.Should().BeFalse();

        _messengerService.Send(new ConsoleSessionConnectedMessage(sessionToken));

        await _launchService.Received(1).SaveFingerprintAsync(ProjectPythonFolder, LaunchFingerprint);
    }

    [Test]
    public async Task Fingerprint_DroppedWhenTheSessionEndsBeforeConnecting()
    {
        var sessionToken = Guid.NewGuid();
        var environment = new Dictionary<string, string>
        {
            [ConsoleEnvironmentVariables.SessionToken] = sessionToken.ToString(),
        };

        var result = await _provider.BuildStartupInvocationAsync(MakeContext(environment: environment));
        result.IsFailure.Should().BeFalse();

        // A session that dies before its client connects must not persist its unproven fingerprint,
        // even if a stray connected message for the same session id arrives afterwards.
        _messengerService.Send(new ConsoleSessionStateChangedMessage(sessionToken, ConsoleSessionRunState.Ended));
        _messengerService.Send(new ConsoleSessionConnectedMessage(sessionToken));

        await _launchService.DidNotReceive().SaveFingerprintAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
