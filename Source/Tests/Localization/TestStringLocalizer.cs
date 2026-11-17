using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Localization;

/// <summary>
/// Builds an IStringLocalizer backed by the application's en-US resources, through TestLocalizerService. A string
/// with arguments is formatted with them.
/// </summary>
internal static class TestStringLocalizer
{
    public static IStringLocalizer Create()
    {
        var localizerService = new TestLocalizerService();
        var stringLocalizer = Substitute.For<IStringLocalizer>();

        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];

            return new LocalizedString(name, localizerService.GetString(name));
        });

        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            var arguments = (object[])callInfo[1];

            return new LocalizedString(name, localizerService.GetString(name, arguments));
        });

        return stringLocalizer;
    }
}
