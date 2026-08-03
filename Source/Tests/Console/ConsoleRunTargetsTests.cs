using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleRunTargetsTests
{
    private const string ProjectFolderPath = "C:/Projects/Acme";

    private static readonly IReadOnlyList<ConsoleRunner> PythonRunners = new[]
    {
        new ConsoleRunner(new[] { ".py", ".ipy" }, "%run \"{resource}\""),
    };

    // filePath defaults to the resource resolved under the project folder, which is what the service
    // supplies. Pass it explicitly to exercise the unresolvable-path fallback.
    private static ConsoleRunCandidate MakeCandidate(
        string resourcePath = "scratch.console",
        bool hasStaleRunners = false,
        IReadOnlyList<ConsoleRunner>? runners = null,
        string? filePath = null)
    {
        return new ConsoleRunCandidate(
            Guid.NewGuid(),
            new ResourceKey(resourcePath),
            filePath ?? $"{ProjectFolderPath}/{resourcePath}",
            runners ?? PythonRunners,
            hasStaleRunners);
    }

    [Test]
    public void ResolveEffectiveRunners_NoConfigRunners_KeepsTheTypeDefaults()
    {
        var runners = ConsoleRunTargets.ResolveEffectiveRunners(Array.Empty<ConsoleDocumentRunner>(), PythonRunners);

        ConsoleRunTargets.FindRunner(runners, ".py")!.CommandTemplate.Should().Be("%run \"{resource}\"");
        ConsoleRunTargets.FindRunner(runners, ".ipy").Should().NotBeNull();
    }

    [Test]
    public void ResolveEffectiveRunners_UnrelatedConfigRunner_LeavesTheDefaultsInPlace()
    {
        // Declaring a runner is additive: the type's own extensions keep working alongside it.
        var configRunners = new[]
        {
            new ConsoleDocumentRunner(new[] { ".sql" }, "%sql {resource}"),
        };

        var runners = ConsoleRunTargets.ResolveEffectiveRunners(configRunners, PythonRunners);

        ConsoleRunTargets.FindRunner(runners, ".sql")!.CommandTemplate.Should().Be("%sql {resource}");
        ConsoleRunTargets.FindRunner(runners, ".py")!.CommandTemplate.Should().Be("%run \"{resource}\"");
    }

    [Test]
    public void ResolveEffectiveRunners_ConfigRunnerForTheSameExtension_ShadowsTheDefault()
    {
        // Only the extension it names: .ipy still resolves to the default runner alongside it.
        var configRunners = new[]
        {
            new ConsoleDocumentRunner(new[] { ".py" }, "%run -i \"{resource}\""),
        };

        var runners = ConsoleRunTargets.ResolveEffectiveRunners(configRunners, PythonRunners);

        ConsoleRunTargets.FindRunner(runners, ".py")!.CommandTemplate.Should().Be("%run -i \"{resource}\"");
        ConsoleRunTargets.FindRunner(runners, ".ipy")!.CommandTemplate.Should().Be("%run \"{resource}\"");
    }

    [Test]
    public void Resolve_CandidateWithAMatchingRunner_IsATarget()
    {
        var candidates = new[] { MakeCandidate() };

        var targets = ConsoleRunTargets.Resolve(candidates, ".py");

        targets.Should().HaveCount(1);
        targets[0].DisplayName.Should().Be("scratch.console");
    }

    [Test]
    public void Resolve_StaleRunners_ExcludesTheCandidate()
    {
        // The REPL those runners target has exited back to the shell prompt.
        var candidates = new[] { MakeCandidate(hasStaleRunners: true) };

        ConsoleRunTargets.Resolve(candidates, ".py").Should().BeEmpty();
    }

    [Test]
    public void Resolve_UnhandledExtension_YieldsNoTargets()
    {
        var candidates = new[] { MakeCandidate() };

        ConsoleRunTargets.Resolve(candidates, ".txt").Should().BeEmpty();
    }

    [Test]
    public void Resolve_UniqueFileNames_AreNotQualifiedByFolder()
    {
        var candidates = new[]
        {
            MakeCandidate(resourcePath: "zebra.console"),
            MakeCandidate(resourcePath: "tools/build.console"),
            MakeCandidate(resourcePath: "apple.console"),
        };

        var targets = ConsoleRunTargets.Resolve(candidates, ".py");

        targets.Select(target => target.DisplayName)
            .Should().Equal("apple.console", "build.console", "zebra.console");
    }

    [Test]
    public void Resolve_SharedFileNameWithOneInTheProjectRoot_QualifiesBoth()
    {
        // A root-level console has no parent folder inside the project, so the project folder itself is
        // what tells it apart. Disambiguating over resource keys alone would leave both bare.
        var candidates = new[]
        {
            MakeCandidate(resourcePath: "python.console"),
            MakeCandidate(resourcePath: "test_prompts/python.console"),
        };

        var targets = ConsoleRunTargets.Resolve(candidates, ".py");

        targets.Select(target => target.DisplayName)
            .Should().Equal("Acme/python.console", "test_prompts/python.console");
    }

    [Test]
    public void Resolve_SharedFileName_QualifiesOnlyThoseTargets()
    {
        var candidates = new[]
        {
            MakeCandidate(resourcePath: "tools/run.console"),
            MakeCandidate(resourcePath: "scripts/run.console"),
            MakeCandidate(resourcePath: "solo.console"),
        };

        var targets = ConsoleRunTargets.Resolve(candidates, ".py");

        // Only the colliding pair carries a folder; the unique one stays a bare file name.
        targets.Select(target => target.DisplayName)
            .Should().Equal("scripts/run.console", "solo.console", "tools/run.console");
    }

    [Test]
    public void Resolve_UnresolvablePath_FallsBackToTheBareFileName()
    {
        // The service supplies the resource name when a path will not resolve, so the target still shows
        // even though it cannot be qualified against the console it collides with.
        var candidates = new[]
        {
            MakeCandidate(resourcePath: "tools/run.console", filePath: "run.console"),
            MakeCandidate(resourcePath: "scripts/run.console"),
        };

        var targets = ConsoleRunTargets.Resolve(candidates, ".py");

        targets.Select(target => target.DisplayName)
            .Should().Equal("run.console", "run.console");
    }

    [Test]
    public void FindRunner_MatchesExtensionCaseInsensitively()
    {
        ConsoleRunTargets.FindRunner(PythonRunners, ".PY").Should().NotBeNull();
        ConsoleRunTargets.FindRunner(PythonRunners, ".py").Should().NotBeNull();
        ConsoleRunTargets.FindRunner(PythonRunners, ".sh").Should().BeNull();
    }

    [Test]
    public void FindRunner_NoRunners_ReturnsNull()
    {
        ConsoleRunTargets.FindRunner(Array.Empty<ConsoleRunner>(), ".py").Should().BeNull();
    }
}
