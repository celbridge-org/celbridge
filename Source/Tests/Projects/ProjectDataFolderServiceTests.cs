using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Celbridge.Platform;
using Celbridge.Projects.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Utilities;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Tests how the data folder's path is derived from the project folder, the project.json it contains, the trust
/// field in project.json, and clearing the folder.
/// </summary>
[TestFixture]
public class ProjectDataFolderServiceTests
{
    private string _testFolderPath = null!;
    private string _appDataFolderPath = null!;
    private string _projectFolderPath = null!;
    private ProjectDataFolderService _service = null!;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectDataFolderServiceTests),
            Guid.NewGuid().ToString("N"));

        _appDataFolderPath = Path.Combine(_testFolderPath, "AppData");
        _projectFolderPath = Path.Combine(_testFolderPath, "Project");

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(_appDataFolderPath);

        _service = new ProjectDataFolderService(
            new NullLogger<ProjectDataFolderService>(),
            appEnvironment,
            TestFileSystem.CreateLocal());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolderPath))
        {
            Directory.Delete(_testFolderPath, recursive: true);
        }
    }

    [Test]
    public void GetDataFolderPath_IsTheFolderNameAndAHashOfTheFolderPath()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");

        var dataFolderPath = _service.GetDataFolderPath(projectFilePath);

        var hash = HashFolderPath(_projectFolderPath);
        dataFolderPath.Should().Be(Path.Combine(_appDataFolderPath, "Projects", $"Project-{hash}"));
    }

    [Test]
    public void GetDataFolderPath_LongFolderName_IsCutTo16Characters()
    {
        var projectFilePath = Path.Combine(_testFolderPath, "AVeryLongProjectFolderName", "Acme.celbridge");

        var folderName = Path.GetFileName(_service.GetDataFolderPath(projectFilePath));

        folderName.Should().StartWith("AVeryLongProject-");
        folderName.Should().HaveLength(16 + 1 + 16);
    }

    [Test]
    public void GetDataFolderPath_RenamedProjectFile_KeepsItsDataFolder()
    {
        var originalPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "Acme.celbridge"));
        var renamedPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "Renamed.celbridge"));

        renamedPath.Should().Be(originalPath);
    }

    [Test]
    public void GetDataFolderPath_TwoProjectFoldersWithOneName_GetAFolderEach()
    {
        var firstPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "One", "Acme", "Acme.celbridge"));
        var secondPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "Two", "Acme", "Acme.celbridge"));

        firstPath.Should().NotBe(secondPath);
    }

    [Test]
    public void GetDataFolderPath_SpellingsThatDifferInCase_ShareAFolderOnlyWhereTheFileSystemIgnoresCase()
    {
        var lowerPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "acme", "Acme.celbridge"));
        var upperPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "ACME", "Acme.celbridge"));

        // The name part keeps its original case, so compare only the hashes.
        var lowerHash = Path.GetFileName(lowerPath).Split('-')[^1];
        var upperHash = Path.GetFileName(upperPath).Split('-')[^1];

        (lowerHash == upperHash).Should().Be(PathComparison.IsCaseInsensitiveFileSystem);
    }

    [Test]
    public void GetDataFolderPath_ProjectAtTheRootOfADrive_IsNamedByTheHashAlone()
    {
        var rootPath = Path.GetPathRoot(Path.GetFullPath(_testFolderPath))!;
        var projectFilePath = Path.Combine(rootPath, "Acme.celbridge");

        var folderName = Path.GetFileName(_service.GetDataFolderPath(projectFilePath));

        folderName.Should().Be(HashFolderPath(rootPath));
    }

    [Test]
    public async Task EnsureDataFolderAsync_CreatesTheFolderWithAProjectJsonNamingTheProjectFolder()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");

        var ensureResult = await _service.EnsureDataFolderAsync(projectFilePath);

        ensureResult.IsSuccess.Should().BeTrue(ensureResult.MessageChain);
        var dataFolderPath = ensureResult.Value;
        dataFolderPath.Should().Be(_service.GetDataFolderPath(projectFilePath));

        var projectJson = await File.ReadAllTextAsync(Path.Combine(dataFolderPath, "project.json"));
        using var document = JsonDocument.Parse(projectJson);
        document.RootElement.GetProperty("projectFolderPath").GetString().Should().Be(_projectFolderPath);
    }

    [Test]
    public async Task EnsureDataFolderAsync_ANewProjectJson_SaysUntrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");

        await _service.EnsureDataFolderAsync(projectFilePath);

        using var document = JsonDocument.Parse(await ReadProjectJsonAsync(projectFilePath));
        document.RootElement.GetProperty("trusted").GetBoolean().Should().BeFalse();
        (await _service.IsTrustedAsync(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task IsTrustedAsync_AProjectJsonWithNoTrustField_IsNotTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        WriteProjectJson(projectFilePath, $$"""{ "projectFolderPath": {{JsonSerializer.Serialize(_projectFolderPath)}} }""");

        (await _service.IsTrustedAsync(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task IsTrustedAsync_ATrustFieldThatSaysTrusted_IsTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        WriteProjectJson(projectFilePath, """{ "projectFolderPath": "x", "trusted": true }""");

        (await _service.IsTrustedAsync(projectFilePath)).Should().BeTrue();
    }

    [Test]
    public async Task IsTrustedAsync_ATrustFieldThatSaysUntrusted_IsNotTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        WriteProjectJson(projectFilePath, """{ "projectFolderPath": "x", "trusted": false }""");

        (await _service.IsTrustedAsync(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task IsTrustedAsync_NoProjectJson_IsNotTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");

        (await _service.IsTrustedAsync(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task IsTrustedAsync_AProjectJsonThatCantBeRead_IsNotTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        WriteProjectJson(projectFilePath, "{ not json");

        (await _service.IsTrustedAsync(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task MarkTrustedAsync_WritesTheTrustAndKeepsTheProjectFolder()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        await _service.EnsureDataFolderAsync(projectFilePath);

        var markResult = await _service.MarkTrustedAsync(projectFilePath);

        markResult.IsSuccess.Should().BeTrue(markResult.MessageChain);
        (await _service.IsTrustedAsync(projectFilePath)).Should().BeTrue();
        using var document = JsonDocument.Parse(await ReadProjectJsonAsync(projectFilePath));
        document.RootElement.GetProperty("projectFolderPath").GetString().Should().Be(_projectFolderPath);
    }

    [Test]
    public async Task MarkTrustedAsync_AProjectJsonThatCantBeRead_IsReplacedWithANewFile()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        WriteProjectJson(projectFilePath, "{ not json");

        var markResult = await _service.MarkTrustedAsync(projectFilePath);

        markResult.IsSuccess.Should().BeTrue(markResult.MessageChain);
        (await _service.IsTrustedAsync(projectFilePath)).Should().BeTrue();
    }

    [Test]
    public async Task MarkTrustedAsync_NoDataFolder_CreatesItTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");

        var markResult = await _service.MarkTrustedAsync(projectFilePath);

        markResult.IsSuccess.Should().BeTrue(markResult.MessageChain);
        (await _service.IsTrustedAsync(projectFilePath)).Should().BeTrue();
    }

    [Test]
    public async Task IsTrustedAsync_AProjectMovedToAnotherFolder_IsNotTrusted()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        await _service.MarkTrustedAsync(projectFilePath);

        // A data folder belongs to one folder path, so the moved project gets a new data folder.
        var movedFolderPath = Path.Combine(_testFolderPath, "Moved");
        Directory.CreateDirectory(movedFolderPath);
        var movedProjectFilePath = Path.Combine(movedFolderPath, "Acme.celbridge");
        File.Move(projectFilePath, movedProjectFilePath);
        await _service.EnsureDataFolderAsync(movedProjectFilePath);

        (await _service.IsTrustedAsync(movedProjectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task EnsureDataFolderAsync_ExistingFolder_KeepsItsContents()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        var dataFolderPath = (await _service.EnsureDataFolderAsync(projectFilePath)).Value;

        var settingsPath = Path.Combine(dataFolderPath, "settings", "workspace_settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "{}");

        var ensureResult = await _service.EnsureDataFolderAsync(projectFilePath);

        ensureResult.IsSuccess.Should().BeTrue(ensureResult.MessageChain);
        File.Exists(settingsPath).Should().BeTrue();
    }

    [Test]
    public async Task EnsureDataFolderAsync_NoProjectFile_FailsAndCreatesNothing()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Missing.celbridge");

        var ensureResult = await _service.EnsureDataFolderAsync(projectFilePath);

        ensureResult.IsFailure.Should().BeTrue();
        Directory.Exists(_service.GetDataFolderPath(projectFilePath)).Should().BeFalse();
    }

    [Test]
    public async Task ClearDataFolderAsync_DeletesTheFolder()
    {
        var projectFilePath = CreateProjectFile("Acme.celbridge");
        var dataFolderPath = (await _service.EnsureDataFolderAsync(projectFilePath)).Value;
        await File.WriteAllTextAsync(Path.Combine(dataFolderPath, "history.txt"), "typed");

        var clearResult = await _service.ClearDataFolderAsync(projectFilePath);

        clearResult.IsSuccess.Should().BeTrue(clearResult.MessageChain);
        Directory.Exists(dataFolderPath).Should().BeFalse();
    }

    [Test]
    public async Task ClearDataFolderAsync_NoFolder_Succeeds()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");

        var clearResult = await _service.ClearDataFolderAsync(projectFilePath);

        clearResult.IsSuccess.Should().BeTrue(clearResult.MessageChain);
    }

    // Hashes the folder's full path, lowercased on a case-insensitive file system.
    private static string HashFolderPath(string folderPath)
    {
        var key = Path.TrimEndingDirectorySeparator(folderPath);
        if (PathComparison.IsCaseInsensitiveFileSystem)
        {
            key = key.ToLowerInvariant();
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexStringLower(hashBytes)[..16];
    }

    private string CreateProjectFile(string fileName)
    {
        Directory.CreateDirectory(_projectFolderPath);
        var projectFilePath = Path.Combine(_projectFolderPath, fileName);
        File.WriteAllText(projectFilePath, string.Empty);

        return projectFilePath;
    }

    private void WriteProjectJson(string projectFilePath, string json)
    {
        var dataFolderPath = _service.GetDataFolderPath(projectFilePath);
        Directory.CreateDirectory(dataFolderPath);
        File.WriteAllText(Path.Combine(dataFolderPath, "project.json"), json);
    }

    private async Task<string> ReadProjectJsonAsync(string projectFilePath)
    {
        var dataFolderPath = _service.GetDataFolderPath(projectFilePath);
        return await File.ReadAllTextAsync(Path.Combine(dataFolderPath, "project.json"));
    }
}
