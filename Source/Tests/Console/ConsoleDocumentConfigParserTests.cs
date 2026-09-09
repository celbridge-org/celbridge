using Celbridge.Console;
using Celbridge.Console.Helpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleDocumentConfigParserTests
{
    // The two built-in session types, as their providers declare them.
    private static readonly IReadOnlyList<ConsoleSessionType> SessionTypes = new[]
    {
        new ConsoleSessionType("shell", new[] { "executable", "arguments" }, Array.Empty<ConsoleRunner>()),
        new ConsoleSessionType("python", new[] { "python_version", "dependencies" }, Array.Empty<ConsoleRunner>()),
    };

    private static Result<ConsoleDocumentConfig> Parse(string toml)
    {
        return ConsoleDocumentConfigParser.Parse(toml, SessionTypes);
    }

    [Test]
    public void Parse_ASessionTypeRegisteredTwice_FailsRatherThanThrowing()
    {
        // A duplicate id is a provider bug the session service catches first, but this method reports every
        // other failure through its Result, so it reports this one too rather than throwing past the caller.
        var duplicated = new[] { SessionTypes[0], SessionTypes[0] };

        var result = ConsoleDocumentConfigParser.Parse("[session]\ntype = \"shell\"", duplicated);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("registered more than once");
    }

    [Test]
    public void Parse_EmptyText_YieldsTheDefaultShellConfig()
    {
        var result = Parse(string.Empty);

        result.IsFailure.Should().BeFalse();
        var config = result.Value;
        config.SessionType.Should().Be("shell");
        config.SessionTypeOptions.Should().BeEmpty();
        config.StartupScript.Should().BeEmpty();
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
            "[session.environment]",
            "BUILD_CONFIG = \"Debug\"",
            "",
            "[session.python]",
            "python_version = \"3.13\"",
            "dependencies = [\"numpy\", \"pandas>=2\"]",
            "",
            "[[session.runner]]",
            "extensions = [\".py\", \".ipy\"]",
            "command = '%run \"{resource}\"'",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        var config = result.Value;
        config.SessionType.Should().Be("python");
        config.WorkingDirectory.Should().Be("tools");
        ConfigTableHelper.ReadText(config.SessionTypeOptions, "python_version").Should().Be("3.13");
        ConfigTableHelper.ReadTextList(config.SessionTypeOptions, "dependencies")
            .Should().Equal("numpy", "pandas>=2");
        config.Environment.Should().ContainKey("BUILD_CONFIG").WhoseValue.Should().Be("Debug");
        config.Runners.Should().HaveCount(1);
        config.Runners[0].Extensions.Should().Equal(".py", ".ipy");
        config.Runners[0].Command.Should().Be("%run \"{resource}\"");
        config.UnknownFields.Should().BeEmpty();
    }

    [Test]
    public void Parse_TableOfAnUnselectedType_DoesNotReachTheSelectedType()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"shell\"",
            "",
            "[session.shell]",
            "executable = \"bash\"",
            "",
            "[session.python]",
            "dependencies = [\"numpy\"]",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        var config = result.Value;
        ConfigTableHelper.ReadText(config.SessionTypeOptions, "executable").Should().Be("bash");
        config.SessionTypeOptions.Should().NotContainKey("dependencies");
        config.UnknownFields.Should().BeEmpty();
    }

    [Test]
    public void Parse_DisabledRunners_MapsToTheHostsQualifiedName()
    {
        // The document names only built-in runners here, because a runner it declares itself has no id, so
        // the key drops the qualifier the host's own property keeps.
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"python\"",
            "disabled_runners = [\"python\"]",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.DisabledBuiltInRunners.Should().Equal("python");
        result.Value.UnknownFields.Should().BeEmpty();
    }

    [Test]
    public void Parse_Triggers_MapsPatternAndCommand()
    {
        var toml = string.Join('\n', new[]
        {
            "[[session.trigger]]",
            "pattern = \"data/**/*.xlsx\"",
            "command = \"%run clean_data.py\"",
            "",
            "[[session.trigger]]",
            "pattern = \"*.py\"",
            "command = '%run \"{resource}\"'",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        var triggers = result.Value.Triggers;
        triggers.Should().HaveCount(2);
        triggers[0].Pattern.Should().Be("data/**/*.xlsx");
        triggers[0].Command.Should().Be("%run clean_data.py");

        triggers[1].Pattern.Should().Be("*.py");
        triggers[1].Command.Should().Be("%run \"{resource}\"");
    }

    [Test]
    public void Parse_TriggerMissingPatternOrCommand_IsDropped()
    {
        var toml = string.Join('\n', new[]
        {
            "[[session.trigger]]",
            "pattern = \"*.xlsx\"",
            "",
            "[[session.trigger]]",
            "command = \"%run clean_data.py\"",
            "",
            "[[session.trigger]]",
            "pattern = \"*.csv\"",
            "command = \"%run load.py\"",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.Triggers.Should().HaveCount(1);
        result.Value.Triggers[0].Pattern.Should().Be("*.csv");
    }

    [Test]
    public void Parse_MultiLineScript_IsTakenVerbatim()
    {
        var toml = string.Join('\n', new[]
        {
            "[session.shell]",
            "script = '''",
            "import numpy as np",
            "# not a comment inside the block",
            "'''",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.StartupScript.Should().Be("import numpy as np\n# not a comment inside the block\n");
    }

    [Test]
    public void Parse_ScriptOfAnUnselectedType_IsNotUsed()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"shell\"",
            "",
            "[session.python]",
            "script = \"import numpy as np\"",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.StartupScript.Should().BeEmpty();
    }

    [Test]
    public void Parse_UnrecognizedKey_IsReportedAndIgnored()
    {
        // A file written before a key was retired (title, for one) still launches.
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"python\"",
            "title = \"Data\"",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.SessionType.Should().Be("python");
        result.Value.UnknownFields.Should().Equal("session.title");
    }

    [Test]
    public void Parse_KeyUnknownToItsType_IsReported()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"shell\"",
            "",
            "[session.shell]",
            "executable = \"bash\"",
            "entrypoint = \"main\"",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.UnknownFields.Should().Equal("session.shell.entrypoint");
    }

    [Test]
    public void Parse_TableOfAnUnregisteredType_IsReportedAndDropped()
    {
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"shell\"",
            "",
            "[session.ruby]",
            "gems = [\"rails\"]",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.SessionTypeOptions.Should().BeEmpty();
        result.Value.UnknownFields.Should().Equal("session.ruby");
    }

    [Test]
    public void Parse_FormatBeforeTheTypeTables_IsReported()
    {
        // The keys a .console file used before each type took its own table. The console still launches,
        // and the advisory names what it ignored.
        var toml = string.Join('\n', new[]
        {
            "[session]",
            "type = \"python\"",
            "startup_script = \"import numpy as np\"",
            "",
            "[session.options]",
            "python_version = \"3.13\"",
        });

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.StartupScript.Should().BeEmpty();
        result.Value.UnknownFields.Should().Contain("session.startup_script");
        result.Value.UnknownFields.Should().Contain("session.options");
    }

    [Test]
    public void Parse_InvalidToml_Fails()
    {
        var result = Parse("[session\ntype =");

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

        var result = Parse(toml);

        result.IsFailure.Should().BeFalse();
        result.Value.SessionType.Should().Be("shell");
    }

    [Test]
    public void Parse_CrlfInput_Parses()
    {
        var result = Parse("[session]\r\ntype = \"shell\"\r\n");

        result.IsFailure.Should().BeFalse();
        result.Value.SessionType.Should().Be("shell");
    }
}
