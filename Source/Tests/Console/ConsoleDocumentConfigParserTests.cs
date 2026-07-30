using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleDocumentConfigParserTests
{
    [Test]
    public void Parse_EmptyText_YieldsTheDefaultShellConfig()
    {
        var result = ConsoleDocumentConfigParser.Parse(string.Empty);

        result.IsFailure.Should().BeFalse();
        var config = result.Value;
        config.Type.Should().Be("shell");
        config.Executable.Should().BeEmpty();
        config.Arguments.Should().BeEmpty();
        config.Environment.Should().BeEmpty();
        config.Runners.Should().BeEmpty();
    }

    [Test]
    public void Parse_FullConfig_MapsEverySection()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"python\"",
            "working_directory = \"tools\"",
            "",
            "[session.options]",
            "python_version = \"3.13\"",
            "arguments = [\"-i\"]",
            "dependencies = [\"numpy\", \"pandas>=2\"]",
            "",
            "[session.environment]",
            "BUILD_CONFIG = \"Debug\"",
            "",
            "[[session.runner]]",
            "extensions = [\".py\", \".ipy\"]",
            "command = '%run \"{script_path}\"'",
        });

        var result = ConsoleDocumentConfigParser.Parse(toml);

        result.IsFailure.Should().BeFalse();
        var config = result.Value;
        config.Type.Should().Be("python");
        config.WorkingDirectory.Should().Be("tools");
        config.PythonVersion.Should().Be("3.13");
        config.Arguments.Should().Equal("-i");
        config.Dependencies.Should().Equal("numpy", "pandas>=2");
        config.Environment.Should().ContainKey("BUILD_CONFIG").WhoseValue.Should().Be("Debug");
        config.Runners.Should().HaveCount(1);
        config.Runners[0].Extensions.Should().Equal(".py", ".ipy");
        config.Runners[0].Command.Should().Be("%run \"{script_path}\"");
    }

    [Test]
    public void Parse_MultiLineStartupScript_IsTakenVerbatim()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "startup_script = '''",
            "import numpy as np",
            "# not a comment inside the block",
            "'''",
        });

        var result = ConsoleDocumentConfigParser.Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.StartupScript.Should().Be("import numpy as np\n# not a comment inside the block\n");
    }

    [Test]
    public void Parse_UnrecognizedKey_IsIgnored()
    {
        // A file written before a key was retired (title, for one) still launches.
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"python\"",
            "title = \"Data\"",
        });

        var result = ConsoleDocumentConfigParser.Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.Type.Should().Be("python");
    }

    [Test]
    public void Parse_InvalidToml_Fails()
    {
        var result = ConsoleDocumentConfigParser.Parse("[session\ntype =");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Invalid .console configuration");
    }

    [Test]
    public void Parse_UnknownKeysAndSections_AreIgnored()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"shell\"",
            "mystery = \"value\"",
            "",
            "[[session.shortcut]]",
            "label = \"Run\"",
            "text = \"pytest\"",
        });

        var result = ConsoleDocumentConfigParser.Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.Type.Should().Be("shell");
    }

    [Test]
    public void Parse_CrlfInput_Parses()
    {
        var result = ConsoleDocumentConfigParser.Parse("[session]\r\ntype = \"shell\"\r\n");

        result.IsFailure.Should().BeFalse();
        result.Value.Type.Should().Be("shell");
    }
}
