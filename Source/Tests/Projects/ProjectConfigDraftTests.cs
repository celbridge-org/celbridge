using Celbridge.Projects;
using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectConfigDraft, the working copy the Project Settings editor mutates and
/// serializes back. Assertions re-parse the output so they test behaviour, not the canonical formatting.
/// </summary>
[TestFixture]
public class ProjectConfigDraftTests
{
    private const string BaseConfig =
        "[celbridge]\n" +
        "celbridge-version = \"0.4.0\"\n" +
        "project-version = \"0.1.0\"\n" +
        "\n" +
        "[celbridge.resources]\n" +
        "ignore-file = \".gitignore\"\n" +
        "add = []\n" +
        "remove = []\n" +
        "lock = []\n" +
        "\n" +
        "[[contribution]]\n" +
        "package = \"celbridge.console\"\n" +
        "contribution = \"console\"\n" +
        "shell = \"python\"\n";

    private static ProjectConfig ApplyAndParse(string text, Action<ProjectConfigDraft> edit)
    {
        var draft = DraftFrom(text);
        edit(draft);

        var parseResult = ProjectConfigParser.ParseFromText(draft.Serialize());
        parseResult.IsSuccess.Should().BeTrue(parseResult.IsFailure ? parseResult.DiagnosticReport : string.Empty);

        return parseResult.Value;
    }

    private static ProjectConfigDraft DraftFrom(string text)
    {
        var sourceResult = ProjectConfigParser.ParseFromText(text);
        sourceResult.IsSuccess.Should().BeTrue(sourceResult.IsFailure ? sourceResult.DiagnosticReport : string.Empty);

        return new ProjectConfigDraft(sourceResult.Value);
    }

    private static ContributionOverride? OverrideOf(ProjectConfig config, string packageName, string contributionId)
    {
        return config.ContributionOverrides
            .SingleOrDefault(contributionOverride => contributionOverride.PackageName == packageName && contributionOverride.ContributionId == contributionId);
    }

    [Test]
    public void Draft_SetPackageDisabled_AddsToDisabledPackages()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetPackageDisabled("acme.pixel-editor", true));
        config.Celbridge.DisabledPackages.Should().Contain("acme.pixel-editor");
    }

    [Test]
    public void Draft_SetPackageDisabledFalse_RemovesFromDisabledPackages()
    {
        var disabled = ApplyAndParse(BaseConfig, draft => draft.SetPackageDisabled("acme.pixel-editor", true));
        var text = ProjectConfigSerializer.Serialize(disabled);

        var config = ApplyAndParse(text, draft => draft.SetPackageDisabled("acme.pixel-editor", false));
        config.Celbridge.DisabledPackages.Should().NotContain("acme.pixel-editor");
    }

    [Test]
    public void Draft_SetContributionDisabled_WritesDisabledMarker()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetContributionDisabled("celbridge.console", "console", true));
        OverrideOf(config, "celbridge.console", "console")!.Disabled.Should().BeTrue();
    }

    [Test]
    public void Draft_ConfigValueWithControlCharacter_RoundTrips()
    {
        // A control character in a string value must be escaped, or the serialized TOML fails to
        // re-parse (ApplyAndParse would throw) and the .celbridge file is corrupted on the next load.
        var value = "before\u001bmiddle\u0007after";
        var config = ApplyAndParse(BaseConfig,
            draft => draft.SetContributionValue("celbridge.console", "console", "banner", new StringEditValue(value)));

        OverrideOf(config, "celbridge.console", "console")!.Config["banner"].Should().Be(value);
    }

    [Test]
    public void Draft_SetContributionEnabled_WritesEnabledMarkerOnNewEntry()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetContributionEnabled("acme.docs", "markdown-preview", true));
        OverrideOf(config, "acme.docs", "markdown-preview")!.Enabled.Should().BeTrue();
    }

    [Test]
    public void Draft_SetContributionValue_SetsConfigKey()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetContributionValue("celbridge.console", "console", "shell", new StringEditValue("pwsh")));
        OverrideOf(config, "celbridge.console", "console")!.Config["shell"].Should().Be("pwsh");
    }

    [Test]
    public void Draft_SetContributionValue_CreatesEntryAndSupportsTypedValues()
    {
        var config = ApplyAndParse(
            BaseConfig,
            draft =>
            {
                draft.SetContributionValue("acme.pixel-editor", "pixel", "grid-size", new IntegerEditValue(16));
                draft.SetContributionValue("acme.pixel-editor", "pixel", "ratio", new FloatEditValue(0.5));
                draft.SetContributionValue("acme.pixel-editor", "pixel", "snap", new BoolEditValue(true));
                draft.SetContributionValue("acme.pixel-editor", "pixel", "deps", new StringListEditValue(new[] { "a", "b" }));
            });

        var contributionOverride = OverrideOf(config, "acme.pixel-editor", "pixel")!;
        contributionOverride.Config["grid-size"].Should().Be(16L);
        contributionOverride.Config["ratio"].Should().Be(0.5);
        contributionOverride.Config["snap"].Should().Be(true);
        ((IReadOnlyList<string>)contributionOverride.Config["deps"]!).Should().Equal("a", "b");
    }

    [Test]
    public void Draft_RemoveContributionValue_DropsEmptiedEntry()
    {
        // The console entry carried only the shell config; removing it leaves no override, so the
        // whole entry is dropped.
        var config = ApplyAndParse(BaseConfig, draft => draft.RemoveContributionValue("celbridge.console", "console", "shell"));
        OverrideOf(config, "celbridge.console", "console").Should().BeNull();
    }

    [Test]
    public void Draft_SetEditorAssociation_AddsEntry()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetEditorAssociation(".PNG", "pixel-art"));
        config.Celbridge.EditorAssociations[".png"].Should().Be("pixel-art");
    }

    [Test]
    public void Draft_RemoveEditorAssociation_RemovesEntry()
    {
        var withOne = ProjectConfigSerializer.Serialize(
            ApplyAndParse(BaseConfig, draft => draft.SetEditorAssociation(".png", "pixel-art")));

        var config = ApplyAndParse(withOne, draft => draft.RemoveEditorAssociation(".png"));
        config.Celbridge.EditorAssociations.Should().NotContainKey(".png");
    }

    [Test]
    public void Draft_SetProjectVersion_UpdatesVersion()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.ProjectVersion = "0.2.0");
        config.Celbridge.ProjectVersion.Should().Be("0.2.0");
    }

    [Test]
    public void Draft_SetDescription_UpdatesDescription()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.Description = "An example project.");
        config.Celbridge.Description.Should().Be("An example project.");
    }

    [Test]
    public void Draft_SetFeatureFlag_PinsFeature()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetFeatureFlag("mcp-tools", false));
        config.Features.Should().ContainKey("mcp-tools");
        config.Features["mcp-tools"].Should().BeFalse();
    }

    [Test]
    public void Draft_RemoveFeatureFlag_ClearsFeature()
    {
        var pinned = ApplyAndParse(BaseConfig, draft => draft.SetFeatureFlag("mcp-tools", false));
        var text = ProjectConfigSerializer.Serialize(pinned);

        var config = ApplyAndParse(text, draft => draft.RemoveFeatureFlag("mcp-tools"));
        config.Features.Should().NotContainKey("mcp-tools");
    }

    [Test]
    public void Draft_SetIgnoreFile_UpdatesIgnoreFile()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.IgnoreFile = ".customignore");
        config.Resources.IgnoreFile.Should().Be(".customignore");
    }

    [Test]
    public void Draft_SeveralEdits_AllLandInTheSerializedConfig()
    {
        var config = ApplyAndParse(
            BaseConfig,
            draft =>
            {
                draft.SetContributionValue("acme.pixel-editor", "pixel", "grid-size", new IntegerEditValue(16));
                draft.SetEditorAssociation(".png", "pixel-art");
                draft.SetPackageDisabled("acme.unwanted", true);
            });

        OverrideOf(config, "acme.pixel-editor", "pixel")!.Config["grid-size"].Should().Be(16L);
        config.Celbridge.EditorAssociations[".png"].Should().Be("pixel-art");
        config.Celbridge.DisabledPackages.Should().Contain("acme.unwanted");
    }
}
