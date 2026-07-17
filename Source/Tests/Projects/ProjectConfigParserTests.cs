using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Unit tests for ProjectConfigParser covering the v2 .celbridge schema: host-level
/// declarations on the [celbridge] table, editor instance tables, and per-entry
/// error recovery for malformed entries.
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
            packages = ["acme-notes", "acme-charts"]
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

            [notepad]
            package = "acme-notes"
            contribution = "notepad"
            title = "Notes"
            icon = "Edit"
            tooltip = "Project notes"
            theme = "dark"
            auto-save = true
            max-items = 42
            scale = 1.5
            tags = ["alpha", "beta"]

            [charts]
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
        config.Celbridge.Packages.Should().Equal("acme-notes", "acme-charts");

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
        config.Instances.Should().HaveCount(2);

        var notepadInstance = config.Instances[0];
        notepadInstance.InstanceId.Should().Be("notepad");
        notepadInstance.PackageName.Should().Be("acme-notes");
        notepadInstance.ContributionId.Should().Be("notepad");
        notepadInstance.Title.Should().Be("Notes");
        notepadInstance.Icon.Should().Be("Edit");
        notepadInstance.Tooltip.Should().Be("Project notes");
        notepadInstance.Config["theme"].Should().Be("dark");
        notepadInstance.Config["auto-save"].Should().Be(true);
        notepadInstance.Config["max-items"].Should().Be(42L);
        notepadInstance.Config["scale"].Should().Be(1.5);

        var tags = notepadInstance.Config["tags"] as IReadOnlyList<string>;
        tags.Should().NotBeNull();
        tags!.Should().Equal("alpha", "beta");

        var chartsInstance = config.Instances[1];
        chartsInstance.InstanceId.Should().Be("charts");
        chartsInstance.PackageName.Should().Be("acme-charts");
        chartsInstance.ContributionId.Should().Be("chart-editor");
        chartsInstance.Title.Should().BeNull();
        chartsInstance.Config.Should().BeEmpty();
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
        config.Project.RequiresPython.Should().Be("3.12");
    }

    [Test]
    public void ParseFromFile_InstanceIdWithInvalidCharacters_SkipsInstanceWithEntryError()
    {
        var content = """
            [My_Notes]
            package = "acme-notes"
            contribution = "notepad"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Instances.Should().BeEmpty();
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("My_Notes");
        config.EntryErrors[0].Message.Should().Contain("lowercase");
    }

    [Test]
    public void ParseFromFile_InstanceMissingPackageKey_SkipsInstanceWithEntryError()
    {
        var content = """
            [notepad]
            contribution = "notepad"
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Instances.Should().BeEmpty();
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("notepad");
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
        config.Instances.Should().BeEmpty();

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
            packages = ["acme-notes"]
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Celbridge.Packages.Should().BeEmpty();
        config.Resources.IgnoreFile.Should().Be(".gitignore");
        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("celbridge.resources");
        config.EntryErrors[0].Message.Should().Contain("Unknown key 'packages'");
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
        config.EntryErrors.Should().Contain(error => error.Message.Contains("leading dot"));
    }

    [Test]
    public void ParseFromFile_TitleWithWrongType_DropsKeyButKeepsInstance()
    {
        var content = """
            [notepad]
            package = "acme-notes"
            contribution = "notepad"
            title = true
            """;
        var configFilePath = WriteConfigFile(content);

        var result = ProjectConfigParser.ParseFromFile(configFilePath, _fileSystem);

        result.IsSuccess.Should().BeTrue();
        var config = result.Value;
        config.Instances.Should().ContainSingle();

        var instance = config.Instances[0];
        instance.InstanceId.Should().Be("notepad");
        instance.Title.Should().BeNull();
        instance.Config.Should().NotContainKey("title");

        config.EntryErrors.Should().ContainSingle();
        config.EntryErrors[0].EntryName.Should().Be("notepad");
        config.EntryErrors[0].Message.Should().Contain("'title' must be a non-empty string");
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
        config.Instances.Should().BeEmpty();
        config.EntryErrors.Should().BeEmpty();
    }

    private string WriteConfigFile(string content)
    {
        var configFilePath = Path.Combine(_testFolderPath, "test.celbridge");
        File.WriteAllText(configFilePath, content);

        return configFilePath;
    }
}
