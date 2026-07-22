using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectConfigParser covering the v2 .celbridge schema: host-level
/// declarations on the [celbridge] table, [[contribution]] override entries, and per-entry
/// error recovery for malformed entries and stray top-level tables.
/// </summary>
[TestFixture]
public class ProjectConfigParserTests
{
    private string _testFolderPath = null!;
    private ILocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectConfigParserTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testFolderPath);

        _fileSystem = TestFileSystem.CreateLocal();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolderPath))
        {
            try
            {
                Directory.Delete(_testFolderPath, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public void ParseFromFile_ValidV2Config_ParsesAllSections()
    {
        var content = """
            [celbridge]
            celbridge-version = "1.0.0"
            project-version = "0.2.0"
            disabled-packages = ["acme-notes", "acme-charts"]
            editor-associations = { ".md" = "acme-notes.markdown", ".TXT" = "acme-notes.text" }
            features = { generative-ai = true, experimental = false }

            [celbridge.resources]
            ignore-file = ".gitignore"
            add = ["Python/.venv/**"]
            remove = [".gitignore"]
            lock = ["assets/**"]

            [project]
            requires-python = ">=3.12"
            dependencies = ["numpy", "pandas"]

            [[contribution]]
            package = "acme-notes"
            contribution = "notepad"
            theme = "dark"
            auto-save = true
            max-items = 42
            scale = 1.5
            tags = ["alpha", "beta"]

            [[contribution]]
            package = "acme-charts"
            contribution = "chart-editor"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.EntryErrors.Should().BeEmpty();

        config.Celbridge.CelbridgeVersion.Should().Be("1.0.0");
        config.Celbridge.ProjectVersion.Should().Be("0.2.0");
        config.Celbridge.DisabledPackages.Should().Equal("acme-notes", "acme-charts");

        // Editor-defaults extensions are lowercased on parse.
        config.Celbridge.EditorAssociations.Should().HaveCount(2);
        config.Celbridge.EditorAssociations[".md"].Should().Be("acme-notes.markdown");
        config.Celbridge.EditorAssociations[".txt"].Should().Be("acme-notes.text");

        config.Features.Should().HaveCount(2);
        config.Features["generative-ai"].Should().BeTrue();
        config.Features["experimental"].Should().BeFalse();

        config.Resources.IgnoreFile.Should().Be(".gitignore");
        config.Resources.Add.Should().Equal("Python/.venv/**");
        config.Resources.Remove.Should().Equal(".gitignore");
        config.Resources.Lock.Should().Equal("assets/**");

        config.Project.RequiresPython.Should().Be(">=3.12");
        config.Project.Dependencies.Should().Equal("numpy", "pandas");

        // Declaration order in the file is preserved.
        config.ContributionOverrides.Should().HaveCount(2);

        var notepadOverride = config.ContributionOverrides[0];
        notepadOverride.PackageName.Should().Be("acme-notes");
        notepadOverride.ContributionId.Should().Be("notepad");
        notepadOverride.Disabled.Should().BeFalse();
        notepadOverride.Enabled.Should().BeFalse();
        notepadOverride.Config["theme"].Should().Be("dark");
        notepadOverride.Config["auto-save"].Should().Be(true);
        notepadOverride.Config["max-items"].Should().Be(42L);
        notepadOverride.Config["scale"].Should().Be(1.5);

        var tags = notepadOverride.Config["tags"] as IReadOnlyList<string>;
        tags.Should().NotBeNull();
        tags!.Should().Equal("alpha", "beta");

        var chartsOverride = config.ContributionOverrides[1];
        chartsOverride.PackageName.Should().Be("acme-charts");
        chartsOverride.ContributionId.Should().Be("chart-editor");
        chartsOverride.Config.Should().BeEmpty();
    }

    [Test]
    public void ParseFromFile_PythonVersionSentinel_MapsToDefaultVersion()
    {
        var content = """
            [project]
            requires-python = "<python-version>"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Project.RequiresPython.Should().Be("3.13");
    }

    [Test]
    public void ParseFromFile_ArbitraryTopLevelTable_IsRejectedWithEntryError()
    {
        // v1 declared editors as arbitrary [editor-id] tables. Those are no longer part
        // of the schema: any top-level table other than the known sections is rejected with an
        // entry error rather than parsed as an editor.
        var content = """
            [my-notes]
            package = "acme-notes"
            contribution = "notepad"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.ContributionOverrides.Should().BeEmpty();
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("my-notes");
        config.EntryErrors[0].Message.Should().Contain("not allowed");
    }

    [Test]
    public void ParseFromFile_ContributionMissingPackageKey_SkipsEntryWithEntryError()
    {
        var content = """
            [[contribution]]
            contribution = "notepad"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.ContributionOverrides.Should().BeEmpty();
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("[[contribution]] #1");
        config.EntryErrors[0].Message.Should().Contain("package");
    }

    [Test]
    public void ParseFromFile_LegacyTopLevelSections_ReportsMovedToCelbridge()
    {
        var content = """
            [features]
            generative-ai = true

            [resources]
            ignore-file = ".customignore"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;

        // The legacy sections are ignored, not parsed from the old location.
        config.Features.Should().BeEmpty();
        config.Resources.IgnoreFile.Should().Be(".gitignore");
        config.ContributionOverrides.Should().BeEmpty();

        config.EntryErrors.Should().HaveCount(2);
        config.EntryErrors.Should().OnlyContain(error => error.Message.Contains("moved to [celbridge]"));
    }

    [Test]
    public void ParseFromFile_StrayKeyOnResourcesTable_ReportsUnknownKey()
    {
        // A flat [celbridge] key appended after the [celbridge.resources] header
        // lands on the resources table per TOML rules; the parser reports it.
        var content = """
            [celbridge]
            celbridge-version = "1.0.0"

            [celbridge.resources]
            ignore-file = ".gitignore"
            disabled-packages = ["acme-notes"]
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Celbridge.DisabledPackages.Should().BeEmpty();
        config.Resources.IgnoreFile.Should().Be(".gitignore");
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("celbridge.resources");
        config.EntryErrors[0].Message.Should().Contain("Unknown key 'disabled-packages'");
    }

    [Test]
    public void ParseFromFile_UnknownCelbridgeKey_IgnoresKeyWithEntryError()
    {
        // The v1 'version' key is the most likely stray key on [celbridge].
        var content = """
            [celbridge]
            version = "1.0.0"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Celbridge.CelbridgeVersion.Should().BeNull();
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("celbridge");
        config.EntryErrors[0].Message.Should().Contain("Unknown key 'version'");
    }

    [Test]
    public void ParseFromFile_InvalidEditorAssociationsEntries_DropsEntriesWithEntryErrors()
    {
        var content = """
            [celbridge]
            editor-associations = { ".md" = 42, "txt" = "acme-notes.text" }
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Celbridge.EditorAssociations.Should().BeEmpty();
        config.EntryErrors.Should().HaveCount(2);
        config.EntryErrors.Should().Contain(error => error.Message.Contains("must name an editor id"));
        config.EntryErrors.Should().Contain(error => error.Message.Contains("well-formed file extension"));
    }

    [Test]
    public void ParseFromFile_ConfigKeyWithUnsupportedShape_DropsKeyButKeepsEntry()
    {
        // A config key whose value is a mixed-type array cannot be encoded, so it is dropped while
        // the rest of the entry survives.
        var content = """
            [[contribution]]
            package = "acme-notes"
            contribution = "notepad"
            tags = ["alpha", 42]
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.ContributionOverrides.Should().ContainSingle();

        var contributionOverride = config.ContributionOverrides[0];
        contributionOverride.PackageName.Should().Be("acme-notes");
        contributionOverride.ContributionId.Should().Be("notepad");
        contributionOverride.Config.Should().NotContainKey("tags");

        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].Message.Should().Contain("must be a list of strings");
    }

    [Test]
    public void ParseFromFile_InvalidTomlSyntax_ReturnsFailure()
    {
        var content = """
            [celbridge
            celbridge-version = "1.0.0"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ParseFromFile_MissingFile_ReturnsEmptyConfig()
    {
        var configFilePath = Path.Combine(_testFolderPath, "missing.celbridge");

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Celbridge.CelbridgeVersion.Should().BeNull();
        config.ContributionOverrides.Should().BeEmpty();
        config.EntryErrors.Should().BeEmpty();
    }

    private string WriteConfigFile(string content)
    {
        var configFilePath = Path.Combine(_testFolderPath, "test.celbridge");
        File.WriteAllText(configFilePath, content);

        return configFilePath;
    }
}
