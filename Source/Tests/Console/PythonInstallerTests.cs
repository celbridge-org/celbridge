using Celbridge.Platform;
using Celbridge.Python;
using Celbridge.Python.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class PythonInstallerTests
{
    [Test]
    public void ReadPyvenvHome_ReturnsTheInterpreterFolder()
    {
        var configText =
            "home = C:\\Store\\cpython-3.13-windows-x86_64-none\r\n" +
            "implementation = CPython\r\n" +
            "version_info = 3.13.12\r\n";

        var home = PythonInstaller.ReadPyvenvHome(configText);

        home.Should().Be(@"C:\Store\cpython-3.13-windows-x86_64-none");
    }

    [Test]
    public void ReadPyvenvHome_WithoutAHomeKey_ReturnsNull()
    {
        var configText =
            "implementation = CPython\n" +
            "extends-environment = /store/cpython-3.13-macos-aarch64-none/bin\n";

        var home = PythonInstaller.ReadPyvenvHome(configText);

        home.Should().BeNull();
    }

    [Test]
    public void BuildToolInstallArguments_ReadsNoConfigurationFile()
    {
        var arguments = PythonInstaller.BuildToolInstallArguments("3.13", "/support/celbridge-0.1.0-py3-none-any.whl");

        arguments.Should().Contain("--no-config");
        arguments.Should().ContainInOrder("--python", "3.13", "--managed-python");
    }

    [Test]
    public void PrepareToolInstallEnvironment_KeepsInheritedUvAndPythonSettingsOut()
    {
        var installer = CreateInstaller();
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = "/usr/bin",
            ["VIRTUAL_ENV"] = "/work/.venv",
            ["UV_PYTHON_PREFERENCE"] = "only-system",
            ["UV_CACHE_DIR"] = "/elsewhere/uv_cache",
            ["UV_INDEX_URL"] = "https://mirror.example/simple",
        };

        var removedNames = installer.PrepareToolInstallEnvironment(environment);

        removedNames.Should().Equal("UV_CACHE_DIR", "UV_PYTHON_PREFERENCE", "VIRTUAL_ENV");
        environment.Should().NotContainKeys("VIRTUAL_ENV", "UV_PYTHON_PREFERENCE");
        environment["UV_CACHE_DIR"].Should().Be(installer.UvCacheFolderPath);
        environment["UV_PYTHON_INSTALL_DIR"].Should().Be(installer.UvPythonInstallFolderPath);
        environment["UV_INDEX_URL"].Should().Be("https://mirror.example/simple");
        environment["PATH"].Should().Be("/usr/bin");
    }

    private static PythonInstaller CreateInstaller()
    {
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(Path.Combine(Path.GetTempPath(), "CelbridgeData"));

        return new PythonInstaller(
            Substitute.For<ILocalFileSystem>(),
            Substitute.For<ILogger<PythonInstaller>>(),
            appEnvironment,
            Substitute.For<IPythonConfigService>());
    }
}
