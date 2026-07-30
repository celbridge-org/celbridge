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
        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            executable,
            arguments ?? Array.Empty<string>(),
            string.Empty,
            new Dictionary<string, string>(),
            ProjectRoot);
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
    public void ShellProvider_ReportsShellTypeWithNoRunners()
    {
        var provider = new ShellSessionProvider();

        provider.TypeId.Should().Be("shell");
        provider.DefaultRunners.Should().BeEmpty();
    }
}
