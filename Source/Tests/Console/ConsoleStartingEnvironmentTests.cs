using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleStartingEnvironmentTests
{
    private static readonly Dictionary<string, string> Inherited = new()
    {
        ["HOME"] = "/Users/someone",
        ["LANG"] = "en_GB.UTF-8",
        ["LC_ALL"] = "en_GB.UTF-8",
        ["HTTPS_PROXY"] = "http://proxy.example:8080",
        ["https_proxy"] = "http://proxy.example:8080",
        ["PATH"] = "/Users/someone/.local/bin:/usr/bin:/bin",
        ["VIRTUAL_ENV"] = "/work/.venv",
        ["OPENAI_API_KEY"] = "secret",
    };

    [Test]
    public void Build_WithTheShellProfile_KeepsEverythingInherited()
    {
        var environment = ConsoleStartingEnvironment.Build(Inherited, useShellProfile: true);

        environment.Should().BeEquivalentTo(Inherited);
    }

    [Test]
    public void Build_Clean_KeepsOnlyTheEssentials()
    {
        var environment = ConsoleStartingEnvironment.Build(Inherited, useShellProfile: false);

        environment.Should().ContainKeys("HOME", "LANG", "LC_ALL", "HTTPS_PROXY", "https_proxy");
        environment.Should().NotContainKeys("VIRTUAL_ENV", "OPENAI_API_KEY");
    }

    [Test]
    public void Build_Clean_StartsFromTheSystemPath()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows reads the system PATH from the registry.");
        }

        var environment = ConsoleStartingEnvironment.Build(Inherited, useShellProfile: false);

        environment["PATH"].Should().Be("/usr/bin:/bin:/usr/sbin:/sbin");
    }
}
