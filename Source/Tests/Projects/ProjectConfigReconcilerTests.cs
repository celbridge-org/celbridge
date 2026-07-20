using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for the reconcile-and-serialize half of the normalize-on-load contract: required and
/// recommended contributions resolve as active without a config entry, optionals stay inert unless
/// enabled, a disable marker turns a recommended contribution off while a required contribution ignores
/// activation markers, disabled packages are honoured, orphaned overrides and redundant config keys
/// are dropped, and the pass is idempotent.
/// </summary>
[TestFixture]
public class ProjectConfigReconcilerTests
{
    private static EditorContribution Contribution(
        string packageName,
        string contributionId,
        bool isUtility = false,
        ActivationPolicy activation = ActivationPolicy.Required,
        IReadOnlyList<ConfigDescriptor>? descriptors = null)
    {
        return new EditorContribution
        {
            Package = new PackageInfo { Name = packageName },
            Id = contributionId,
            Activation = activation,
            ConfigDescriptors = descriptors ?? Array.Empty<ConfigDescriptor>(),
            UtilityDescriptor = isUtility ? new UtilityDescriptor { ResourceExtension = "._x" } : null,
        };
    }

    private static ProjectConfig EmptyConfig()
    {
        return new ProjectConfig
        {
            Celbridge = new CelbridgeSection { CelbridgeVersion = "0.4.0", ProjectVersion = "0.1.0" },
        };
    }

    private static ContributionOverride? OverrideOf(ProjectConfig config, string packageName, string contributionId)
    {
        return config.ContributionOverrides
            .SingleOrDefault(contributionOverride => contributionOverride.PackageName == packageName && contributionOverride.ContributionId == contributionId);
    }

    private static bool IsActive(ProjectConfigReconcileResult result, string packageName, string contributionId)
    {
        return result.ActiveContributions
            .Any(active => active.Contribution.Package.Name == packageName && active.Contribution.Id == contributionId);
    }

    private static ResolvedContribution ActiveOf(ProjectConfigReconcileResult result, string packageName, string contributionId)
    {
        return result.ActiveContributions
            .Single(active => active.Contribution.Package.Name == packageName && active.Contribution.Id == contributionId);
    }

    [Test]
    public void Reconcile_ActivatesDefaultActiveContributionsWithoutOverrides()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note"),
            Contribution("acme.tools", "console", isUtility: true),
        };

        var result = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);

        result.ActiveContributions.Should().HaveCount(2);
        IsActive(result, "acme.notes", "note").Should().BeTrue();
        IsActive(result, "acme.tools", "console").Should().BeTrue();

        // A default-active contribution at default config produces no override entry.
        result.Config.ContributionOverrides.Should().BeEmpty();
    }

    [Test]
    public void Reconcile_OptionalContributionStaysInert()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note", activation: ActivationPolicy.Optional),
        };

        var result = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);

        result.ActiveContributions.Should().BeEmpty();
        result.Config.ContributionOverrides.Should().BeEmpty();
    }

    [Test]
    public void Reconcile_OptionalContributionWithEnabledMarkerIsActive()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note", activation: ActivationPolicy.Optional),
        };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new() { PackageName = "acme.notes", ContributionId = "note", Enabled = true },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        IsActive(result, "acme.notes", "note").Should().BeTrue();
        OverrideOf(result.Config, "acme.notes", "note")!.Enabled.Should().BeTrue();
    }

    [Test]
    public void Reconcile_RecommendedContributionIsActiveByDefault()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note", activation: ActivationPolicy.Recommended),
        };

        var result = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);

        IsActive(result, "acme.notes", "note").Should().BeTrue();
        result.Config.ContributionOverrides.Should().BeEmpty();
    }

    [Test]
    public void Reconcile_DisabledRecommendedIsNotActive()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note", activation: ActivationPolicy.Recommended),
        };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new() { PackageName = "acme.notes", ContributionId = "note", Disabled = true },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        IsActive(result, "acme.notes", "note").Should().BeFalse();
        OverrideOf(result.Config, "acme.notes", "note")!.Disabled.Should().BeTrue();
    }

    [Test]
    public void Reconcile_RequiredContributionCannotBeDisabled()
    {
        // A required contribution has no per-project off switch, so a disable marker is ignored and
        // dropped with a warning; the contribution stays active and no override is persisted.
        var contributions = new List<EditorContribution> { Contribution("acme.notes", "note") };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new() { PackageName = "acme.notes", ContributionId = "note", Disabled = true },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        IsActive(result, "acme.notes", "note").Should().BeTrue();
        result.Config.ContributionOverrides.Should().BeEmpty();
        result.Warnings.Should().Contain(warning => warning.Contains("acme.notes/note"));
    }

    [Test]
    public void Reconcile_DisabledPackageContributesNothing()
    {
        var contributions = new List<EditorContribution> { Contribution("acme.notes", "note") };
        var config = EmptyConfig() with
        {
            Celbridge = EmptyConfig().Celbridge with { DisabledPackages = new[] { "acme.notes" } },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        result.ActiveContributions.Should().BeEmpty();
        result.Config.Celbridge.DisabledPackages.Should().Equal("acme.notes");
    }

    [Test]
    public void Reconcile_DropsStaleDisabledPackage()
    {
        var contributions = new List<EditorContribution> { Contribution("acme.notes", "note") };
        var config = EmptyConfig() with
        {
            Celbridge = EmptyConfig().Celbridge with { DisabledPackages = new[] { "acme.gone" } },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        result.Config.Celbridge.DisabledPackages.Should().BeEmpty();
    }

    [Test]
    public void Reconcile_DropsOrphanedOverride()
    {
        var contributions = new List<EditorContribution> { Contribution("acme.notes", "note") };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new() { PackageName = "acme.old", ContributionId = "old", Disabled = true },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        IsActive(result, "acme.notes", "note").Should().BeTrue();
        result.Config.ContributionOverrides.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(warning => warning.Contains("acme.old"));
    }

    [Test]
    public void Reconcile_DropsUnknownAndDefaultValuedConfigKeys()
    {
        var descriptors = new List<ConfigDescriptor>
        {
            new() { Key = "shell", Type = ConfigValueType.String, DefaultValue = "python" },
        };
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.tools", "console", isUtility: true, descriptors: descriptors),
        };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new()
                {
                    PackageName = "acme.tools",
                    ContributionId = "console",
                    Config = new Dictionary<string, object?>
                    {
                        ["shell"] = "python",   // equals default, dropped
                        ["mystery"] = "x",      // unknown, dropped
                    },
                },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        IsActive(result, "acme.tools", "console").Should().BeTrue();
        // Both keys dropped, so the entry no longer deviates and is not persisted.
        OverrideOf(result.Config, "acme.tools", "console").Should().BeNull();
        result.Warnings.Should().Contain(warning => warning.Contains("mystery"));
    }

    [Test]
    public void Reconcile_KeepsNonDefaultConfigKey()
    {
        var descriptors = new List<ConfigDescriptor>
        {
            new() { Key = "shell", Type = ConfigValueType.String, DefaultValue = "python" },
        };
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.tools", "console", isUtility: true, descriptors: descriptors),
        };
        var config = EmptyConfig() with
        {
            ContributionOverrides = new List<ContributionOverride>
            {
                new()
                {
                    PackageName = "acme.tools",
                    ContributionId = "console",
                    Config = new Dictionary<string, object?> { ["shell"] = "pwsh" },
                },
            },
        };

        var result = ProjectConfigReconciler.Reconcile(config, contributions);

        ActiveOf(result, "acme.tools", "console").Config["shell"].Should().Be("pwsh");
        OverrideOf(result.Config, "acme.tools", "console")!.Config["shell"].Should().Be("pwsh");
    }

    [Test]
    public void Reconcile_ActiveSetFollowsDiscoveryOrder()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.a", "editor-a"),
            Contribution("acme.b", "util-b", isUtility: true),
        };

        var result = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);

        var ids = result.ActiveContributions.Select(active => active.Contribution.Id).ToList();
        ids.Should().Equal("editor-a", "util-b");
    }

    [Test]
    public void Reconcile_SameContributionIdAcrossPackagesBothActive()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.one", "note"),
            Contribution("acme.two", "note"),
        };

        var result = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);

        result.ActiveContributions.Should().HaveCount(2);
        IsActive(result, "acme.one", "note").Should().BeTrue();
        IsActive(result, "acme.two", "note").Should().BeTrue();
    }

    [Test]
    public void Reconcile_IsIdempotent()
    {
        var contributions = new List<EditorContribution>
        {
            Contribution("acme.notes", "note"),
            Contribution("acme.tools", "console", isUtility: true),
            Contribution("acme.opt", "extra", activation: ActivationPolicy.Optional),
        };

        var first = ProjectConfigReconciler.Reconcile(EmptyConfig(), contributions);
        var firstText = ProjectConfigSerializer.Serialize(first.Config);

        var second = ProjectConfigReconciler.Reconcile(first.Config, contributions);
        var secondText = ProjectConfigSerializer.Serialize(second.Config);

        secondText.Should().Be(firstText);
    }

    [Test]
    public void Serialize_RoundTripsThroughParser()
    {
        var config = EmptyConfig() with
        {
            Celbridge = EmptyConfig().Celbridge with
            {
                DisabledPackages = new[] { "acme.off" },
                EditorAssociations = new Dictionary<string, string> { [".png"] = "pixel" },
            },
            ContributionOverrides = new List<ContributionOverride>
            {
                new()
                {
                    PackageName = "acme.tools",
                    ContributionId = "console",
                    Config = new Dictionary<string, object?>
                    {
                        ["shell"] = "pwsh",
                        ["count"] = 3L,
                        ["ratio"] = 0.5,
                        ["flag"] = true,
                        ["deps"] = new List<string> { "a", "b" },
                    },
                },
                new()
                {
                    PackageName = "acme.notes",
                    ContributionId = "note",
                    Disabled = true,
                },
            },
        };

        var text = ProjectConfigSerializer.Serialize(config);
        var parseResult = ProjectConfigParser.ParseFromText(text);
        parseResult.IsSuccess.Should().BeTrue(parseResult.IsFailure ? parseResult.DiagnosticReport : string.Empty);
        var reparsed = parseResult.Value;

        reparsed.Celbridge.DisabledPackages.Should().Equal("acme.off");
        reparsed.Celbridge.EditorAssociations[".png"].Should().Be("pixel");
        var console = OverrideOf(reparsed, "acme.tools", "console")!;
        console.Config["shell"].Should().Be("pwsh");
        console.Config["count"].Should().Be(3L);
        console.Config["flag"].Should().Be(true);
        ((IReadOnlyList<string>)console.Config["deps"]!).Should().Equal("a", "b");
        OverrideOf(reparsed, "acme.notes", "note")!.Disabled.Should().BeTrue();
    }
}
