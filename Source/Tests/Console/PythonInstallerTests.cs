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
}
