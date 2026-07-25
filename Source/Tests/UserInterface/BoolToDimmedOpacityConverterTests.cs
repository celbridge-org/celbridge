using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests the boolean-to-opacity mapping that dims an inactive item while keeping
/// it visible (e.g. a read-only resource or an off feature flag).
/// </summary>
[TestFixture]
public class BoolToDimmedOpacityConverterTests
{
    [Test]
    public void True_MapsToFullOpacity()
    {
        var converter = new BoolToDimmedOpacityConverter();

        var opacity = converter.Convert(true, typeof(double), null!, null!);

        opacity.Should().Be(1.0);
    }

    [Test]
    public void False_MapsToDimmedOpacity()
    {
        var converter = new BoolToDimmedOpacityConverter();

        var opacity = converter.Convert(false, typeof(double), null!, null!);

        opacity.Should().Be(0.5);
    }
}
