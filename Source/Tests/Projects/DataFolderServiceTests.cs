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
/// Covers the data folder's key, the project.json it holds, and clearing it for a new project.
/// </summary>
[TestFixture]
public class DataFolderServiceTests
{
    private string _testFolderPath = null!;
    private string _appDataFolderPath = null!;
    private string _projectFolderPath = null!;
    private DataFolderService _service = null!;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DataFolderServiceTests),
            Guid.NewGuid().ToString("N"));

        _appDataFolderPath = Path.Combine(_testFolderPath, "AppData");
        _projectFolderPath = Path.Combine(_testFolderPath, "Project");

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.LocalApplicationDataFolderPath.Returns(_appDataFolderPath);

        _service = new DataFolderService(
            new NullLogger<DataFolderService>(),
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
    public void GetDataFolderPath_IsTheProjectNameAndAHashOfThePath()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");

        var dataFolderPath = _service.GetDataFolderPath(projectFilePath);

        // The hash is of the full path, folded to lower case where the file system ignores case.
        var key = PathComparison.IsCaseInsensitiveFileSystem
            ? projectFilePath.ToLowerInvariant()
            : projectFilePath;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

        dataFolderPath.Should().Be(Path.Combine(_appDataFolderPath, "Projects", $"Acme-{hash}"));
    }

    [Test]
    public void GetDataFolderPath_LongName_IsCutTo16Characters()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "AVeryLongProjectNameIndeed.celbridge");

        var folderName = Path.GetFileName(_service.GetDataFolderPath(projectFilePath));

        folderName.Should().StartWith("AVeryLongProject-");
        folderName.Should().HaveLength(16 + 1 + 16);
    }

    [Test]
    public void GetDataFolderPath_TwoProjectFilesInOneFolder_GetAFolderEach()
    {
        var firstPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "First.celbridge"));
        var secondPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "Second.celbridge"));

        firstPath.Should().NotBe(secondPath);
    }

    [Test]
    public void GetDataFolderPath_TwoProjectsWithOneName_GetAFolderEach()
    {
        var firstPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "One", "Acme.celbridge"));
        var secondPath = _service.GetDataFolderPath(Path.Combine(_testFolderPath, "Two", "Acme.celbridge"));

        firstPath.Should().NotBe(secondPath);
    }

    [Test]
    public void GetDataFolderPath_SpellingsThatDifferInCase_ShareAFolderOnlyWhereTheFileSystemIgnoresCase()
    {
        var lowerPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "acme.celbridge"));
        var upperPath = _service.GetDataFolderPath(Path.Combine(_projectFolderPath, "ACME.celbridge"));

        // The name keeps the spelling it was given, so only the hash is compared.
        var lowerHash = Path.GetFileName(lowerPath).Split('-')[^1];
        var upperHash = Path.GetFileName(upperPath).Split('-')[^1];

        (lowerHash == upperHash).Should().Be(PathComparison.IsCaseInsensitiveFileSystem);
    }

    [Test]
    public async Task EnsureDataFolderAsync_CreatesTheFolderWithAProjectJsonNamingTheProjectFile()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");

        var ensureResult = await _service.EnsureDataFolderAsync(projectFilePath);

        ensureResult.IsSuccess.Should().BeTrue(ensureResult.MessageChain);
        var dataFolderPath = ensureResult.Value;
        dataFolderPath.Should().Be(_service.GetDataFolderPath(projectFilePath));

        var projectJson = await File.ReadAllTextAsync(Path.Combine(dataFolderPath, "project.json"));
        using var document = JsonDocument.Parse(projectJson);
        document.RootElement.GetProperty("projectFilePath").GetString().Should().Be(projectFilePath);
    }

    [Test]
    public async Task EnsureDataFolderAsync_ExistingFolder_KeepsItsContents()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");
        var dataFolderPath = (await _service.EnsureDataFolderAsync(projectFilePath)).Value;

        var settingsPath = Path.Combine(dataFolderPath, "settings", "workspace_settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "{}");

        var ensureResult = await _service.EnsureDataFolderAsync(projectFilePath);

        ensureResult.IsSuccess.Should().BeTrue(ensureResult.MessageChain);
        File.Exists(settingsPath).Should().BeTrue();
    }

    [Test]
    public async Task ClearDataFolderAsync_DeletesTheFolder()
    {
        var projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");
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
}
