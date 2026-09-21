using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Workspace;

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
        "hide = []\n" +
        "search-exclude = []\n" +
        "\n" +
        "[[contribution]]\n" +
        "package = \"celbridge.console\"\n" +
        "contribution = \"console\"\n" +
        "shell = \"python\"\n";

    // Also checks that the draft's text reads back exactly as written. The editor takes a saved file that
    // reads back differently for an outside change and reloads every section from it, so a draft that
    // writes anything a load drops wipes out what the user is typing.
    private static ProjectConfig ApplyAndParse(string text, Action<ProjectConfigDraft> edit)
    {
        var draft = DraftFrom(text);
        edit(draft);

        var writtenText = draft.Serialize();
        var parseResult = ProjectConfigParser.ParseFromText(writtenText);
        parseResult.IsSuccess.Should().BeTrue(parseResult.IsFailure ? parseResult.DiagnosticReport : string.Empty);

        ProjectConfigSerializer.Serialize(parseResult.Value).Should().Be(writtenText);

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
    public void Draft_PreservesTheDataFolder_AcrossAnUnrelatedEdit()
    {
        // The data folder has no editor surface, so an edit made through the Project Settings editor
        // must carry it through the serialize rather than reset the project to the default folder.
        var sourceConfig =
            "[celbridge]\n" +
            "data-folder = \"variant-a\"\n";

        var config = ApplyAndParse(sourceConfig, draft => draft.SetPackageDisabled("acme.pixel-editor", true));

        config.Celbridge.DataFolder.Should().Be("variant-a");
    }

    [Test]
    public void Draft_PreservesTheDownloadsFolder_AcrossAnUnrelatedEdit()
    {
        // The draft rebuilds the resources table on every serialize, so an edit to another section must
        // carry the downloads folder through rather than reset the project to the default folder.
        var sourceConfig =
            "[celbridge]\n" +
            "\n" +
            "[celbridge.resources]\n" +
            "downloads-folder = \"assets/incoming\"\n";

        var config = ApplyAndParse(sourceConfig, draft => draft.SetPackageDisabled("acme.pixel-editor", true));

        config.Resources.DownloadsFolder.Should().Be("assets/incoming");
    }

    [Test]
    public void Draft_SetDownloadsFolder_WritesThePathItNames()
    {
        var config = ApplyAndParse(BaseConfig, draft => draft.SetDownloadsFolder(" /assets/incoming/ "));

        config.Resources.DownloadsFolder.Should().Be("assets/incoming");
    }

    [TestCase("", Description = "a cleared field")]
    [TestCase("downloads", Description = "the default folder")]
    [TestCase("../outside", Description = "a path that is not a folder path, which a load would drop")]
    public void Draft_SetDownloadsFolderThatNamesNoOtherFolder_WritesNoKey(string folderPath)
    {
        var config = ApplyAndParse(BaseConfig, draft =>
        {
            draft.SetDownloadsFolder("assets/incoming");
            draft.SetDownloadsFolder(folderPath);
        });

        config.Resources.DownloadsFolder.Should().BeEmpty();
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

    [TestCase("0.2", Description = "a version with two parts")]
    [TestCase("latest", Description = "text that is not a version")]
    public void Draft_SetProjectVersionThatIsNotAVersion_WritesNoKey(string projectVersion)
    {
        // A load drops a version that does not parse, which leaves the project at the default version.
        var config = ApplyAndParse(BaseConfig, draft => draft.ProjectVersion = projectVersion);

        config.Celbridge.ProjectVersion.Should().BeNull();
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
        var config = ApplyAndParse(BaseConfig, draft => draft.SetFeatureFlag("webview-dev-tools", false));
        config.Features.Should().ContainKey("webview-dev-tools");
        config.Features["webview-dev-tools"].Should().BeFalse();
    }

    [Test]
    public void Draft_RemoveFeatureFlag_ClearsFeature()
    {
        var pinned = ApplyAndParse(BaseConfig, draft => draft.SetFeatureFlag("webview-dev-tools", false));
        var text = ProjectConfigSerializer.Serialize(pinned);

        var config = ApplyAndParse(text, draft => draft.RemoveFeatureFlag("webview-dev-tools"));
        config.Features.Should().NotContainKey("webview-dev-tools");
    }

    [Test]
    public void Draft_SetHidePatterns_UpdatesHideList()
    {
        var patterns = new List<string>
        {
            ".gitignore",
            "drafts/**",
        };

        var config = ApplyAndParse(BaseConfig, draft => draft.SetHidePatterns(patterns));
        config.Resources.Hide.Should().Equal(".gitignore", "drafts/**");
    }

    [Test]
    public void Draft_SetSearchExcludePatterns_DropsABlankRow()
    {
        // A row the user has not typed into yet matches nothing, so it writes no entry.
        var patterns = new List<string>
        {
            "node_modules",
            "   ",
        };

        var config = ApplyAndParse(BaseConfig, draft => draft.SetSearchExcludePatterns(patterns));
        config.Resources.SearchExclude.Should().Equal("node_modules");
    }

    [Test]
    public void Draft_SetDocumentShortcuts_RoundTripsInTheOrderGiven()
    {
        // The rail draws the shortcuts in list order, so a reorder is only recorded if the serialized
        // entries keep the order the section set.
        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "docs/guide.md", Icon = "bs-book", Area = WorkspaceArea.Bottom, HasRailButton = false, OpenOnLoad = true },
            new() { Resource = "readme.md" },
        };

        var config = ApplyAndParse(BaseConfig, draft => draft.SetDocumentShortcuts(documentShortcuts));

        config.DocumentShortcuts.Should().HaveCount(2);
        config.DocumentShortcuts[0].Resource.Should().Be("docs/guide.md");
        config.DocumentShortcuts[0].Icon.Should().Be("bs-book");
        config.DocumentShortcuts[0].Area.Should().Be(WorkspaceArea.Bottom);
        config.DocumentShortcuts[0].HasRailButton.Should().BeFalse();
        config.DocumentShortcuts[0].OpenOnLoad.Should().BeTrue();
        config.DocumentShortcuts[1].Resource.Should().Be("readme.md");
        config.DocumentShortcuts[1].Icon.Should().BeEmpty();
        config.DocumentShortcuts[1].Area.Should().Be(WorkspaceArea.Main);
        config.DocumentShortcuts[1].HasRailButton.Should().BeTrue();
        config.DocumentShortcuts[1].OpenOnLoad.Should().BeFalse();
    }

    [Test]
    public void Draft_SetDocumentShortcuts_DropsAnEntryWithNoResource()
    {
        // A card the user has not filled in yet opens nothing, so it writes no entry.
        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "readme.md" },
            new() { Resource = string.Empty, Icon = "bs-book" },
        };

        var config = ApplyAndParse(BaseConfig, draft => draft.SetDocumentShortcuts(documentShortcuts));

        config.DocumentShortcuts.Should().ContainSingle();
        config.DocumentShortcuts[0].Resource.Should().Be("readme.md");
    }

    [TestCase("../outside.md", Description = "a path that leaves the project")]
    [TestCase("docs\\guide.md", Description = "a path written with backslashes")]
    public void Draft_SetDocumentShortcuts_DropsAnEntryWhoseResourceIsNotAResourceKey(string resource)
    {
        // A load drops a shortcut naming something that is not a resource key, so the card the user is
        // still correcting writes no entry.
        var documentShortcuts = new List<DocumentShortcut>
        {
            new() { Resource = "readme.md" },
            new() { Resource = resource, Icon = "bs-book" },
        };

        var config = ApplyAndParse(BaseConfig, draft => draft.SetDocumentShortcuts(documentShortcuts));

        config.DocumentShortcuts.Should().ContainSingle();
        config.DocumentShortcuts[0].Resource.Should().Be("readme.md");
    }

    [Test]
    public void Draft_PreservesDocumentShortcuts_AcrossAnUnrelatedEdit()
    {
        var sourceConfig =
            BaseConfig +
            "\n" +
            "[[shortcut]]\n" +
            "resource = \"readme.md\"\n";

        var config = ApplyAndParse(sourceConfig, draft => draft.Description = "Unrelated edit.");

        config.DocumentShortcuts.Should().ContainSingle();
        config.DocumentShortcuts[0].Resource.Should().Be("readme.md");
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
