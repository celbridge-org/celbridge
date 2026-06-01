using Celbridge.FileSystem.Services;
using Celbridge.Logging;
using NSubstitute;

namespace Celbridge.Tests.FileSystem;

/// <summary>
/// Test-side factory for an <see cref="ILocalFileSystem"/> instance that talks to the
/// real disk. The production <see cref="LocalFileSystem"/> takes a logger, so
/// this is the one-liner test setups use to drop the noise.
/// </summary>
internal static class TestFileSystem
{
    public static LocalFileSystem CreateLocal()
    {
        return new LocalFileSystem(Substitute.For<ILogger<LocalFileSystem>>());
    }
}
