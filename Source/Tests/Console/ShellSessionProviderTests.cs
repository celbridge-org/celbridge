using Celbridge.Console;
using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ShellSessionProviderTests
{
    private static string ProjectRoot =>
        OperatingSystem.IsWindows() ? @"C:\Projects\Demo" : "/projects/demo";

    private static ConsoleSessionContext MakeContext(
        string executable = "",
        IReadOnlyList<string>? arguments = null)
    {
        var typeOptions = new Dictionary<string, object?>
        {
            ["executable"] = executable,
            ["arguments"] = arguments ?? Array.Empty<string>(),
        };

        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            string.Empty,
            new Dictionary<string, string>(),
            ProjectRoot,
            string.Empty,
            typeOptions);
    }

    [Test]
    public async Task BuildStartupInvocation_BlankExecutable_InjectsNothing()
    {
        var provider = new ShellSessionProvider();

        var result = await provider.BuildStartupInvocationAsync(MakeContext(executable: string.Empty));

        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(ConsoleStartupInvocation.None);
    }

    [Test]
    public async Task BuildStartupInvocation_WhitespaceExecutable_InjectsNothing()
    {
        var provider = new ShellSessionProvider();

        var result = await provider.BuildStartupInvocationAsync(MakeContext(executable: "   "));

        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(ConsoleStartupInvocation.None);
    }

    [Test]
    public async Task BuildStartupInvocation_ExecutableWithArguments_PassesThrough()
    {
        var provider = new ShellSessionProvider();
        var arguments = new[] { "-NoLogo", "-File", "a b.ps1" };

        var result = await provider.BuildStartupInvocationAsync(MakeContext(executable: "pwsh", arguments: arguments));

        result.IsFailure.Should().BeFalse();
        var command = result.Value;
        command.Executable.Should().Be("pwsh");
        command.Arguments.Should().Equal(arguments);
    }

    [Test]
    public async Task BuildStartupInvocation_NoOptionsTable_InjectsNothing()
    {
        var provider = new ShellSessionProvider();
        var context = new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            string.Empty,
            new Dictionary<string, string>(),
            ProjectRoot,
            string.Empty,
            new Dictionary<string, object?>());

        var result = await provider.BuildStartupInvocationAsync(context);

        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(ConsoleStartupInvocation.None);
    }

    [Test]
    public void ShellProvider_ReportsShellTypeWithNoRunners()
    {
        var provider = new ShellSessionProvider();

        provider.SessionType.TypeId.Should().Be("shell");
        provider.SessionType.BuiltInRunners.Should().BeEmpty();
        provider.SessionType.OptionKeys.Should().Equal("executable", "arguments");
    }
}
