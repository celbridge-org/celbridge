using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Commands;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the clear browsing data command. The clear itself happens inside the web view platform, so the
/// tests assert whether the command asks for it and how it reports the outcome.
/// </summary>
[TestFixture]
public class ClearBrowsingDataCommandTests
{
    private IWebViewPlatform _webViewPlatform = null!;

    [SetUp]
    public void Setup()
    {
        _webViewPlatform = Substitute.For<IWebViewPlatform>();

        _webViewPlatform.SupportsLiveBrowsingDataClear.Returns(true);
    }

    [Test]
    public async Task PlatformCannotClear_FailsWithoutClearing()
    {
        _webViewPlatform.SupportsLiveBrowsingDataClear.Returns(false);

        var result = await CreateCommand().ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await _webViewPlatform.DidNotReceive().ClearBrowsingDataAsync();
    }

    [Test]
    public async Task PlatformCanClear_ClearsAndSucceeds()
    {
        var result = await CreateCommand().ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _webViewPlatform.Received(1).ClearBrowsingDataAsync();
    }

    [Test]
    public async Task ClearThrows_ReportsAFailure()
    {
        _webViewPlatform.ClearBrowsingDataAsync()
            .Returns(Task.FromException(new InvalidOperationException("The clear did not complete")));

        var result = await CreateCommand().ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    private ClearBrowsingDataCommand CreateCommand()
    {
        return new ClearBrowsingDataCommand(
            new NullLogger<ClearBrowsingDataCommand>(),
            _webViewPlatform);
    }
}
