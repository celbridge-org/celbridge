using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleStartupScriptTests
{
    [Test]
    public void SplitLines_BlankScript_YieldsNoLines()
    {
        ConsoleStartupScript.SplitLines(null).Should().BeEmpty();
        ConsoleStartupScript.SplitLines(string.Empty).Should().BeEmpty();
        ConsoleStartupScript.SplitLines("  \n \n").Should().BeEmpty();
    }

    [Test]
    public void SplitLines_SplitsOnEveryLineEndingStyle()
    {
        ConsoleStartupScript.SplitLines("a\nb").Should().Equal("a", "b");
        ConsoleStartupScript.SplitLines("a\r\nb").Should().Equal("a", "b");
        ConsoleStartupScript.SplitLines("a\rb").Should().Equal("a", "b");
    }

    [Test]
    public void SplitLines_DropsTrailingBlankLines()
    {
        ConsoleStartupScript.SplitLines("import numpy\n\n").Should().Equal("import numpy");
    }

    [Test]
    public void SplitLines_KeepsInteriorBlankLines()
    {
        // A blank line is what closes an indented block at a REPL prompt, so it must survive.
        var script = "for i in range(3):\n    print(i)\n\nprint('done')";

        ConsoleStartupScript.SplitLines(script)
            .Should().Equal("for i in range(3):", "    print(i)", string.Empty, "print('done')");
    }

    [Test]
    public void SplitLines_PreservesLeadingIndentation()
    {
        ConsoleStartupScript.SplitLines("    indented").Should().Equal("    indented");
    }
}
