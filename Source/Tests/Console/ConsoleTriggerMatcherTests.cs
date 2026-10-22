using Celbridge.Console.Helpers;
using Celbridge.Resources;
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

    private static ResourceChangeBatch Batch(params ResourceChange[] changes)
    {
        return new ResourceChangeBatch(ResourceKey.DefaultRoot, changes);
    }

    private static ResourceChange FileAt(string path)
    {
        return new ResourceChange(new ResourceKey(path), ResourceItemState.File);
    }

    [Test]
    public void Resolve_MatchingFile_SubstitutesTheResourcePath()
    {
        var trigger = Trigger("src/**/*.py", "%run \"{resource}\"");

        var invocations = ConsoleTriggerMatcher.Resolve(trigger, Batch(FileAt("src/tools/build.py")));

        invocations.Should().Equal("%run \"src/tools/build.py\"");
    }

    [Test]
    public void Resolve_CommandWithoutThePlaceholder_RunsVerbatim()
    {
        // The data-cleaning shape: the changed file is the input, and the command names its own script.
        var trigger = Trigger("data/**/*.xlsx", "%run clean_data.py");

        var invocations = ConsoleTriggerMatcher.Resolve(trigger, Batch(FileAt("data/raw/messy.xlsx")));

        invocations.Should().Equal("%run clean_data.py");
    }

    [Test]
    public void Resolve_NonMatchingFile_ResolvesNothing()
    {
        var trigger = Trigger("data/**/*.xlsx", "%run clean_data.py");

        var invocations = ConsoleTriggerMatcher.Resolve(trigger, Batch(FileAt("docs/notes.md")));

        invocations.Should().BeEmpty();
    }

    [Test]
    public void Resolve_SeveralMatchingFiles_ResolvesEachOne()
    {
        var trigger = Trigger("*.py", "%run \"{resource}\"");

        var invocations = ConsoleTriggerMatcher.Resolve(
            trigger,
            Batch(FileAt("src/build.py"), FileAt("docs/notes.md"), FileAt("tools/lint.py")));

        invocations.Should().Equal("%run \"src/build.py\"", "%run \"tools/lint.py\"");
    }

    [Test]
    public void Resolve_AFolderOrAnAbsentKey_ResolvesNothing()
    {
        // Both reach the trigger's subscription because the pattern can match a path inside them.
        var trigger = Trigger("src/**", "!make");

        var invocations = ConsoleTriggerMatcher.Resolve(
            trigger,
            Batch(
                new ResourceChange(new ResourceKey("src/build"), ResourceItemState.Folder),
                new ResourceChange(new ResourceKey("src/old.py"), ResourceItemState.Absent)));

        invocations.Should().BeEmpty();
    }

    [Test]
    public void Resolve_BatchOutsideTheProjectRoot_ResolvesNothing()
    {
        // Triggers watch the project tree, so the temp and logs roots cannot fire one.
        var trigger = Trigger("*.log", "%run report.py");
        var change = new ResourceChange(new ResourceKey("logs:session.log"), ResourceItemState.File);

        var invocations = ConsoleTriggerMatcher.Resolve(trigger, new ResourceChangeBatch("logs", new[] { change }));

        invocations.Should().BeEmpty();
    }
}
