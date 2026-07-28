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
        IReadOnlyList<string>? arguments = null,
        string workingDirectory = "",
        IReadOnlyDictionary<string, string>? environment = null)
    {
        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            executable,
            arguments ?? Array.Empty<string>(),
            workingDirectory,
            environment ?? new Dictionary<string, string>(),
            ProjectRoot);
    }

    [Test]
    public async Task BuildLaunchSpec_BlankExecutable_UsesPlatformDefaultShell()
    {
        var provider = new ShellSessionProvider();

        var result = await provider.BuildLaunchSpecAsync(MakeContext(executable: string.Empty));

        result.IsFailure.Should().BeFalse();
        var spec = result.Value;

        if (OperatingSystem.IsWindows())
        {
            spec.CommandLine.Should().Be("powershell.exe");
        }
        else
        {
            // The default shell resolves to $SHELL or a rooted fallback, quoted for /bin/sh -c.
            spec.CommandLine.Should().NotBeNullOrEmpty();
            spec.CommandLine.Should().Contain("/");
        }
    }

    [Test]
    public async Task BuildLaunchSpec_ExplicitExecutableWithArguments_QuotesTokens()
    {
        var provider = new ShellSessionProvider();
        var arguments = new[] { "-NoLogo", "-File", "a b.ps1" };

        var result = await provider.BuildLaunchSpecAsync(MakeContext(executable: "pwsh", arguments: arguments));

        result.IsFailure.Should().BeFalse();
        var spec = result.Value;

        if (OperatingSystem.IsWindows())
        {
            spec.CommandLine.Should().Be("pwsh -NoLogo -File \"a b.ps1\"");
        }
        else
        {
            spec.CommandLine.Should().Be("'pwsh' '-NoLogo' '-File' 'a b.ps1'");
        }
    }

    [Test]
    public async Task BuildLaunchSpec_RelativeWorkingDirectory_ResolvesAgainstProjectRoot()
    {
        var provider = new ShellSessionProvider();

        var result = await provider.BuildLaunchSpecAsync(MakeContext(workingDirectory: "tools"));

        result.IsFailure.Should().BeFalse();
        var expected = Path.GetFullPath(Path.Combine(ProjectRoot, "tools"));
        result.Value.WorkingDirectory.Should().Be(expected);
    }

    [Test]
    public async Task BuildLaunchSpec_AbsoluteWorkingDirectory_UsedAsIs()
    {
        var provider = new ShellSessionProvider();
        var absolute = OperatingSystem.IsWindows() ? @"D:\build" : "/tmp/build";

        var result = await provider.BuildLaunchSpecAsync(MakeContext(workingDirectory: absolute));

        result.IsFailure.Should().BeFalse();
        result.Value.WorkingDirectory.Should().Be(absolute);
    }

    [Test]
    public async Task BuildLaunchSpec_EmptyWorkingDirectory_DefaultsToProjectRoot()
    {
        var provider = new ShellSessionProvider();

        var result = await provider.BuildLaunchSpecAsync(MakeContext(workingDirectory: string.Empty));

        result.IsFailure.Should().BeFalse();
        result.Value.WorkingDirectory.Should().Be(ProjectRoot);
    }

    [Test]
    public async Task BuildLaunchSpec_Environment_PassedThrough()
    {
        var provider = new ShellSessionProvider();
        var environment = new Dictionary<string, string>
        {
            ["BUILD_CONFIG"] = "Debug",
            ["FLAG"] = "1",
        };

        var result = await provider.BuildLaunchSpecAsync(MakeContext(environment: environment));

        result.IsFailure.Should().BeFalse();
        result.Value.Environment.Should().BeEquivalentTo(environment);
    }

    [Test]
    public void ShellProvider_ReportsShellTypeWithNoHostBindingAndNoRunners()
    {
        var provider = new ShellSessionProvider();

        provider.TypeId.Should().Be("shell");
        provider.HostBinding.Should().Be(ConsoleHostBinding.None);
        provider.DefaultRunners.Should().BeEmpty();
    }
}
