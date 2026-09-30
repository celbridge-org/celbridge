using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleReadyMarkerTests
{
    private static readonly ConsoleShell Zsh = new("/bin/zsh", ConsoleShellFamily.Posix);
    private static readonly ConsoleShell PowerShell = new("powershell.exe", ConsoleShellFamily.PowerShell);

    [TestCase(false)]
    [TestCase(true)]
    public void For_PosixShell_IsAnInvisibleOscWithOrWithoutACommand(bool hasCommand)
    {
        var marker = ConsoleReadyMarker.For(Zsh, hasCommand);

        marker.Should().NotBeNull();
        marker!.ScanText.Should().Be("\u001b]7000;CELBRIDGE-CONSOLE-READY\u0007");
        marker.PersistsOnScreen.Should().BeFalse();
    }

    [Test]
    public void For_PowerShell_IsEmittedOnlyBeforeACommand()
    {
        ConsoleReadyMarker.For(PowerShell, hasCommand: false).Should().BeNull();

        var marker = ConsoleReadyMarker.For(PowerShell, hasCommand: true);
        marker.Should().NotBeNull();
        marker!.ScanText.Should().Be("␄");
        marker.PersistsOnScreen.Should().BeTrue();
    }

    [Test]
    public void PosixPrintfSource_EscapesTheBytesTheHostScansFor()
    {
        // The literal source must differ from the bytes printf writes, so an echo of it never matches.
        var printed = ConsoleReadyMarker.PosixPrintfSource.Replace(@"\033", "\u001b").Replace(@"\007", "\u0007");

        printed.Should().Be(ConsoleReadyMarker.For(Zsh, hasCommand: false)!.ScanText);
        ConsoleReadyMarker.PosixPrintfSource.Should().NotContain("\u001b");
    }
}
