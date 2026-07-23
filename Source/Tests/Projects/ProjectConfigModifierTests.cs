using Celbridge.Projects;
using Celbridge.Projects.Services;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectConfigModifier, which applies Project Settings edits by mutating the parsed
/// model and serializing it back. Assertions re-parse the output so they test behaviour, not the
/// canonical formatting.
/// </summary>
[TestFixture]
public class ProjectConfigModifierTests
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

    private static ProjectConfig ApplyAndParse(string text, params ProjectConfigEdit[] edits)
    {
        var writeResult = ProjectConfigModifier.ApplyEdits(text, edits);
        writeResult.IsSuccess.Should().BeTrue(writeResult.IsFailure ? writeResult.DiagnosticReport : string.Empty);

        var parseResult = ProjectConfigParser.ParseFromText(writeResult.Value);
        parseResult.IsSuccess.Should().BeTrue(parseResult.IsFailure ? parseResult.DiagnosticReport : string.Empty);

        return parseResult.Value;
    }

    private static ContributionOverride? OverrideOf(ProjectConfig config, string packageName, string contributionId)
    {
        return config.ContributionOverrides
            .SingleOrDefault(contributionOverride => contributionOverride.PackageName == packageName && contributionOverride.ContributionId == contributionId);
    }

    [Test]
    public void ApplyEdits_SetPackageDisabled_AddsToDisabledPackages()
    {
        var config = ApplyAndParse(BaseConfig, new SetPackageDisabledEdit("acme.pixel-editor", true));
        config.Celbridge.DisabledPackages.Should().Contain("acme.pixel-editor");
    }

    [Test]
    public void ApplyEdits_SetPackageDisabledFalse_RemovesFromDisabledPackages()
    {
        var disabled = ApplyAndParse(BaseConfig, new SetPackageDisabledEdit("acme.pixel-editor", true));
        var text = ProjectConfigSerializer.Serialize(disabled);

        var config = ApplyAndParse(text, new SetPackageDisabledEdit("acme.pixel-editor", false));
        config.Celbridge.DisabledPackages.Should().NotContain("acme.pixel-editor");
    }

    [Test]
    public void ApplyEdits_SetContributionDisabled_WritesDisabledMarker()
    {
        var config = ApplyAndParse(BaseConfig, new SetContributionDisabledEdit("celbridge.console", "console", true));
        OverrideOf(config, "celbridge.console", "console")!.Disabled.Should().BeTrue();
    }

    [Test]
    public void ApplyEdits_ConfigValueWithControlCharacter_RoundTrips()
    {
        // A control character in a string value must be escaped, or the serialized TOML fails to
        // re-parse (ApplyAndParse would throw) and the .celbridge file is corrupted on the next load.
        var value = "before\u001bmiddle\u0007after";
        var config = ApplyAndParse(BaseConfig,
            new SetContributionValueEdit("celbridge.console", "console", "banner", new StringEditValue(value)));

        OverrideOf(config, "celbridge.console", "console")!.Config["banner"].Should().Be(value);
    }

    [Test]
    public void ApplyEdits_SetContributionEnabled_WritesEnabledMarkerOnNewEntry()
    {
        var config = ApplyAndParse(BaseConfig, new SetContributionEnabledEdit("acme.docs", "markdown-preview", true));
        OverrideOf(config, "acme.docs", "markdown-preview")!.Enabled.Should().BeTrue();
    }

    [Test]
    public void ApplyEdits_SetContributionValue_SetsConfigKey()
    {
        var config = ApplyAndParse(BaseConfig, new SetContributionValueEdit("celbridge.console", "console", "shell", new StringEditValue("pwsh")));
        OverrideOf(config, "celbridge.console", "console")!.Config["shell"].Should().Be("pwsh");
    }

    [Test]
    public void ApplyEdits_SetContributionValue_CreatesEntryAndSupportsTypedValues()
    {
        var config = ApplyAndParse(
            BaseConfig,
            new SetContributionValueEdit("acme.pixel-editor", "pixel", "grid-size", new IntegerEditValue(16)),
            new SetContributionValueEdit("acme.pixel-editor", "pixel", "ratio", new FloatEditValue(0.5)),
            new SetContributionValueEdit("acme.pixel-editor", "pixel", "snap", new BoolEditValue(true)),
            new SetContributionValueEdit("acme.pixel-editor", "pixel", "deps", new StringListEditValue(new[] { "a", "b" })));

        var contributionOverride = OverrideOf(config, "acme.pixel-editor", "pixel")!;
        contributionOverride.Config["grid-size"].Should().Be(16L);
        contributionOverride.Config["ratio"].Should().Be(0.5);
        contributionOverride.Config["snap"].Should().Be(true);
        ((IReadOnlyList<string>)contributionOverride.Config["deps"]!).Should().Equal("a", "b");
    }

    [Test]
    public void ApplyEdits_RemoveContributionValue_DropsEmptiedEntry()
    {
        // The console entry carried only the shell config; removing it leaves no override, so the
        // whole entry is dropped.
        var config = ApplyAndParse(BaseConfig, new RemoveContributionValueEdit("celbridge.console", "console", "shell"));
        OverrideOf(config, "celbridge.console", "console").Should().BeNull();
    }

    [Test]
    public void ApplyEdits_SetEditorAssociation_AddsEntry()
    {
        var config = ApplyAndParse(BaseConfig, new SetEditorAssociationEdit(".PNG", "pixel-art"));
        config.Celbridge.EditorAssociations[".png"].Should().Be("pixel-art");
    }

    [Test]
    public void ApplyEdits_RemoveEditorAssociation_RemovesEntry()
    {
        var withOne = ProjectConfigModifier.ApplyEdits(BaseConfig, [new SetEditorAssociationEdit(".png", "pixel-art")]).Value;

        var config = ApplyAndParse(withOne, new RemoveEditorAssociationEdit(".png"));
        config.Celbridge.EditorAssociations.Should().NotContainKey(".png");
    }

    [Test]
    public void ApplyEdits_SetProjectVersion_UpdatesVersion()
    {
        var config = ApplyAndParse(BaseConfig, new SetProjectVersionEdit("0.2.0"));
        config.Celbridge.ProjectVersion.Should().Be("0.2.0");
    }

    [Test]
    public void ApplyEdits_SetDescription_UpdatesDescription()
    {
        var config = ApplyAndParse(BaseConfig, new SetDescriptionEdit("An example project."));
        config.Celbridge.Description.Should().Be("An example project.");
    }

    [Test]
    public void ApplyEdits_SetIgnoreFile_UpdatesIgnoreFile()
    {
        var config = ApplyAndParse(BaseConfig, new SetIgnoreFileEdit(".customignore"));
        config.Resources.IgnoreFile.Should().Be(".customignore");
    }

    [Test]
    public void ApplyEdits_SyntaxError_Fails()
    {
        var invalid = "[celbridge\ncelbridge-version = \"0.4.0\"\n";
        var result = ProjectConfigModifier.ApplyEdits(invalid, [new SetPackageDisabledEdit("acme.pixel-editor", true)]);
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ApplyEdits_BatchOfEdits_AppliesInOrder()
    {
        var config = ApplyAndParse(
            BaseConfig,
            new SetContributionValueEdit("acme.pixel-editor", "pixel", "grid-size", new IntegerEditValue(16)),
            new SetEditorAssociationEdit(".png", "pixel-art"),
            new SetPackageDisabledEdit("acme.unwanted", true));

        OverrideOf(config, "acme.pixel-editor", "pixel")!.Config["grid-size"].Should().Be(16L);
        config.Celbridge.EditorAssociations[".png"].Should().Be("pixel-art");
        config.Celbridge.DisabledPackages.Should().Contain("acme.unwanted");
    }
}
