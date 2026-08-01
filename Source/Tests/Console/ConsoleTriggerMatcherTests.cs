using Celbridge.Console.Helpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleTriggerMatcherTests
{
    private static ConsoleTrigger Trigger(string pattern, string command)
    {
        var matcher = ResourcePathMatcher.Compile(pattern);

        return new ConsoleTrigger(matcher, command);
    }

    [Test]
    public void Resolve_MatchingResource_SubstitutesTheResourcePath()
    {
        var triggers = new List<ConsoleTrigger>
        {
            Trigger("src/**/*.py", "%run \"{resource}\""),
        };

        var invocations = ConsoleTriggerMatcher.Resolve(triggers, new ResourceKey("src/tools/build.py"));

        invocations.Should().Equal("%run \"src/tools/build.py\"");
    }

    [Test]
    public void Resolve_CommandWithoutThePlaceholder_RunsVerbatim()
    {
        // The data-cleaning shape: the changed file is the input, and the command names its own script.
        var triggers = new List<ConsoleTrigger>
        {
            Trigger("data/**/*.xlsx", "%run clean_data.py"),
        };

        var invocations = ConsoleTriggerMatcher.Resolve(triggers, new ResourceKey("data/raw/messy.xlsx"));

        invocations.Should().Equal("%run clean_data.py");
    }

    [Test]
    public void Resolve_NonMatchingResource_ResolvesNothing()
    {
        var triggers = new List<ConsoleTrigger>
        {
            Trigger("data/**/*.xlsx", "%run clean_data.py"),
        };

        var invocations = ConsoleTriggerMatcher.Resolve(triggers, new ResourceKey("docs/notes.md"));

        invocations.Should().BeEmpty();
    }

    [Test]
    public void Resolve_SeveralMatchingTriggers_ResolvesEachOne()
    {
        var triggers = new List<ConsoleTrigger>
        {
            Trigger("*.py", "%run \"{resource}\""),
            Trigger("src/**", "!make"),
            Trigger("*.csv", "%run load.py"),
        };

        var invocations = ConsoleTriggerMatcher.Resolve(triggers, new ResourceKey("src/build.py"));

        invocations.Should().Equal("%run \"src/build.py\"", "!make");
    }

    [Test]
    public void Resolve_ResourceOutsideTheProjectRoot_ResolvesNothing()
    {
        // Triggers watch the project tree, so the temp and logs roots cannot fire one.
        var triggers = new List<ConsoleTrigger>
        {
            Trigger("*.log", "%run report.py"),
        };

        var invocations = ConsoleTriggerMatcher.Resolve(triggers, new ResourceKey("logs:session.log"));

        invocations.Should().BeEmpty();
    }
}
