using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Commands;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Verifies that SetThemeCommand forwards its theme to IUserInterfaceService, which owns persisting and
/// applying it.
/// </summary>
[TestFixture]
public class SetThemeCommandTests
{
    private IUserInterfaceService _userInterfaceService = null!;
    private SetThemeCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _userInterfaceService = Substitute.For<IUserInterfaceService>();

        _command = new SetThemeCommand(_userInterfaceService);
    }

    [Test]
    public async Task ExecuteAsync_SetsTheRequestedTheme()
    {
        _command.Theme = ApplicationColorTheme.Dark;

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _userInterfaceService.Received(1).SetTheme(ApplicationColorTheme.Dark);
    }

    [Test]
    public async Task ExecuteAsync_SystemIsForwardedLikeAnyOtherTheme()
    {
        // System is the default, so a command that left Theme unset would look identical to one asking
        // for System. The command forwards it either way.
        _command.Theme = ApplicationColorTheme.System;

        await _command.ExecuteAsync();

        _userInterfaceService.Received(1).SetTheme(ApplicationColorTheme.System);
    }
}
