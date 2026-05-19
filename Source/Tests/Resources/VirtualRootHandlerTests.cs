using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services.Roots;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class VirtualRootHandlerTests
{
    private string? _tempBacking;
    private string? _logsBacking;

    [SetUp]
    public void Setup()
    {
        _tempBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(VirtualRootHandlerTests)}_temp");
        _logsBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(VirtualRootHandlerTests)}_logs");

        foreach (var path in new[] { _tempBacking, _logsBacking })
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            Directory.CreateDirectory(path);
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var path in new[] { _tempBacking, _logsBacking })
        {
            if (path is not null && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    [Test]
    public void TempRootHandlerResolvesUnderBackingLocation()
    {
        Guard.IsNotNull(_tempBacking);
        var validator = new PathValidator();
        var handler = new TempRootHandler(_tempBacking, validator);

        handler.RootName.Should().Be("temp");
        handler.BackingLocation.Should().Be(_tempBacking);
        handler.Capabilities.IsWritable.Should().BeTrue();
        handler.Capabilities.IsWatched.Should().BeTrue();

        var resolveResult = handler.Resolve(ResourceKey.Create("temp:staging/foo/bar.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(Path.Combine(_tempBacking, "staging", "foo", "bar.txt")));
    }

    [Test]
    public void LogsRootHandlerResolvesUnderBackingLocation()
    {
        Guard.IsNotNull(_logsBacking);
        var validator = new PathValidator();
        var handler = new LogsRootHandler(_logsBacking, validator);

        handler.RootName.Should().Be("logs");
        handler.BackingLocation.Should().Be(_logsBacking);
        handler.Capabilities.IsWritable.Should().BeTrue();
        handler.Capabilities.IsWatched.Should().BeTrue();

        var resolveResult = handler.Resolve(ResourceKey.Create("logs:session.log"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(Path.Combine(_logsBacking, "session.log")));
    }

    [Test]
    public void TempRootHandlerResolvesRootOnlyKeyToBackingFolder()
    {
        Guard.IsNotNull(_tempBacking);
        var validator = new PathValidator();
        var handler = new TempRootHandler(_tempBacking, validator);

        var resolveResult = handler.Resolve(ResourceKey.Create("temp:"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(_tempBacking).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Test]
    public void HandlersShareValidatorWithoutCrossContamination()
    {
        Guard.IsNotNull(_tempBacking);
        Guard.IsNotNull(_logsBacking);

        // Both handlers share a single PathValidator instance, just like ResourceService wires them in production.
        var validator = new PathValidator();
        var tempHandler = new TempRootHandler(_tempBacking, validator);
        var logsHandler = new LogsRootHandler(_logsBacking, validator);

        // Same path-portion key resolves under each handler to that handler's backing location.
        var key = ResourceKey.Create("session.log");
        var resolveTemp = tempHandler.Resolve(key);
        var resolveLogs = logsHandler.Resolve(key);

        resolveTemp.IsSuccess.Should().BeTrue();
        resolveLogs.IsSuccess.Should().BeTrue();
        resolveTemp.Value.Should().StartWith(Path.GetFullPath(_tempBacking));
        resolveLogs.Value.Should().StartWith(Path.GetFullPath(_logsBacking));
    }

    [Test]
    public void GetResourceKeyOnHandlerReturnsRootPrefixedKey()
    {
        Guard.IsNotNull(_tempBacking);
        var validator = new PathValidator();
        var handler = new TempRootHandler(_tempBacking, validator);

        var absolutePath = Path.Combine(_tempBacking, "staging", "foo", "bar.txt");
        var keyResult = handler.GetResourceKey(absolutePath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be("temp");
        keyResult.Value.Path.Should().Be("staging/foo/bar.txt");
        keyResult.Value.FullKey.Should().Be("temp:staging/foo/bar.txt");
    }

    [Test]
    public void GetResourceKeyReturnsRootOnlyKeyWhenPathIsBackingLocation()
    {
        Guard.IsNotNull(_logsBacking);
        var validator = new PathValidator();
        var handler = new LogsRootHandler(_logsBacking, validator);

        var keyResult = handler.GetResourceKey(_logsBacking);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be("logs");
        keyResult.Value.Path.Should().Be("");
        keyResult.Value.FullKey.Should().Be("logs:");
    }

    [Test]
    public void GetResourceKeyFailsForPathOutsideBackingLocation()
    {
        Guard.IsNotNull(_tempBacking);
        var validator = new PathValidator();
        var handler = new TempRootHandler(_tempBacking, validator);

        // A path under the logs backing folder is not under temp's backing.
        Guard.IsNotNull(_logsBacking);
        var absolutePath = Path.Combine(_logsBacking, "foo.txt");

        var keyResult = handler.GetResourceKey(absolutePath);
        keyResult.IsFailure.Should().BeTrue();
        keyResult.FirstErrorMessage.Should().Contain("not under root 'temp'");
    }
}
