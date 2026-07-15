using Celbridge.Resources.Services.Roots;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class VirtualRootHandlerTests
{
    private string? _tempBacking;
    private string? _logsBacking;
    private string? _utilsBacking;

    [SetUp]
    public void Setup()
    {
        _tempBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(VirtualRootHandlerTests)}_temp");
        _logsBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(VirtualRootHandlerTests)}_logs");
        _utilsBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(VirtualRootHandlerTests)}_utils");

        foreach (var path in new[] { _tempBacking, _logsBacking, _utilsBacking })
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
        foreach (var path in new[] { _tempBacking, _logsBacking, _utilsBacking })
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
        var handler = new TempRootHandler(_tempBacking);

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
        var handler = new LogsRootHandler(_logsBacking);

        handler.RootName.Should().Be("logs");
        handler.BackingLocation.Should().Be(_logsBacking);
        handler.Capabilities.IsWritable.Should().BeTrue();
        // The logs root is deliberately unwatched: it is rewritten constantly and nothing consumes its events.
        handler.Capabilities.IsWatched.Should().BeFalse();

        var resolveResult = handler.Resolve(ResourceKey.Create("logs:session.log"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(Path.Combine(_logsBacking, "session.log")));
    }

    [Test]
    public void UtilsRootHandlerResolvesUnderBackingLocation()
    {
        Guard.IsNotNull(_utilsBacking);
        var handler = new UtilsRootHandler(_utilsBacking);

        handler.RootName.Should().Be("utils");
        handler.BackingLocation.Should().Be(_utilsBacking);
        handler.Capabilities.IsWritable.Should().BeTrue();
        handler.Capabilities.IsWatched.Should().BeTrue();

        var resolveResult = handler.Resolve(ResourceKey.Create("utils:settings._notepad"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(Path.Combine(_utilsBacking, "settings._notepad")));
    }

    [Test]
    public void UtilsRootHandlerRoundTripsResourceKey()
    {
        Guard.IsNotNull(_utilsBacking);
        var handler = new UtilsRootHandler(_utilsBacking);

        var absolutePath = Path.Combine(_utilsBacking, "settings._notepad");
        var keyResult = handler.GetResourceKey(absolutePath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be("utils");
        keyResult.Value.Path.Should().Be("settings._notepad");
        keyResult.Value.FullKey.Should().Be("utils:settings._notepad");
    }

    [Test]
    public void TempRootHandlerResolvesRootOnlyKeyToBackingFolder()
    {
        Guard.IsNotNull(_tempBacking);
        var handler = new TempRootHandler(_tempBacking);

        var resolveResult = handler.Resolve(ResourceKey.Create("temp:"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(
            Path.GetFullPath(_tempBacking).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Test]
    public void HandlersResolveSameKeyToDifferentBackings()
    {
        Guard.IsNotNull(_tempBacking);
        Guard.IsNotNull(_logsBacking);

        var tempHandler = new TempRootHandler(_tempBacking);
        var logsHandler = new LogsRootHandler(_logsBacking);

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
        var handler = new TempRootHandler(_tempBacking);

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
        var handler = new LogsRootHandler(_logsBacking);

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
        var handler = new TempRootHandler(_tempBacking);

        // A path under the logs backing folder is not under temp's backing.
        Guard.IsNotNull(_logsBacking);
        var absolutePath = Path.Combine(_logsBacking, "foo.txt");

        var keyResult = handler.GetResourceKey(absolutePath);
        keyResult.IsFailure.Should().BeTrue();
        keyResult.FirstErrorMessage.Should().Contain("not under root 'temp'");
    }
}
