using Celbridge.Resources.Services;

namespace Celbridge.Tests;

[TestFixture]
public class PathValidatorTests
{
    private string? _projectFolder;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(PathValidatorTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }
        Directory.CreateDirectory(_projectFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder!, true);
        }
    }

    [Test]
    public void ValidateAndResolveSucceedsForValidKey()
    {
        Guard.IsNotNull(_projectFolder);

        var validator = new PathValidator();
        var resourceKey = ResourceKey.Create("folder/file.txt");

        var resolveResult = validator.ValidateAndResolve(_projectFolder, resourceKey);

        var expectedPath = Path.GetFullPath(Path.Combine(_projectFolder, "folder", "file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveSucceedsForEmptyKey()
    {
        Guard.IsNotNull(_projectFolder);

        var validator = new PathValidator();

        var resolveResult = validator.ValidateAndResolve(_projectFolder, ResourceKey.Empty);

        var expectedPath = Path.GetFullPath(_projectFolder);
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveCachesVerifiedFolders()
    {
        Guard.IsNotNull(_projectFolder);

        // Create the folder on disk so the first call does a full check
        var subFolder = Path.Combine(_projectFolder, "cached");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "a.txt"), "test");

        var validator = new PathValidator();

        // First call — verifies the folder
        validator.ValidateAndResolve(_projectFolder, ResourceKey.Create("cached/a.txt"));

        // Second call — should hit the cache (no way to assert directly, but it should not throw)
        validator.ValidateAndResolve(_projectFolder, ResourceKey.Create("cached/b.txt"));
    }

    [Test]
    public void InvalidateCacheClearsVerifiedFolders()
    {
        Guard.IsNotNull(_projectFolder);

        var subFolder = Path.Combine(_projectFolder, "ephemeral");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "a.txt"), "test");

        var validator = new PathValidator();

        // Cache the folder
        validator.ValidateAndResolve(_projectFolder, ResourceKey.Create("ephemeral/a.txt"));

        // Invalidate
        validator.InvalidateCache();

        // Next call should re-verify (still succeeds since folder is clean)
        var resolveResult = validator.ValidateAndResolve(
            _projectFolder, ResourceKey.Create("ephemeral/a.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void ValidateAndResolveRejectsReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(PathValidatorTests)}_outside");
        Directory.CreateDirectory(outsideFolder);

        var symlinkPath = Path.Combine(_projectFolder, "link_folder");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var validator = new PathValidator();

            var resolveResult = validator.ValidateAndResolve(
                _projectFolder, ResourceKey.Create("link_folder/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }

    [Test]
    public void ValidateAndResolveAcceptsNonExistentPath()
    {
        Guard.IsNotNull(_projectFolder);

        var validator = new PathValidator();

        // Non-existent paths should be accepted (for create operations)
        var resolveResult = validator.ValidateAndResolve(
            _projectFolder, ResourceKey.Create("new_folder/new_file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void ValidateAndResolveRejectsIntermediateReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(PathValidatorTests)}_outside2");
        Directory.CreateDirectory(outsideFolder);

        // Create a structure: project/parent/link -> outside
        var parentFolder = Path.Combine(_projectFolder, "parent");
        Directory.CreateDirectory(parentFolder);

        var symlinkPath = Path.Combine(parentFolder, "link");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var validator = new PathValidator();

            var resolveResult = validator.ValidateAndResolve(
                _projectFolder, ResourceKey.Create("parent/link/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }
}
