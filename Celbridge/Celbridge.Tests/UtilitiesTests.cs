using Celbridge.BaseLibrary.Utilities;
using Celbridge.Messaging.Services;

namespace Celbridge.Tests;

[TestFixture]
public class UtilitiesTests
{
    [Test]
    public void ICanValidateResourceKeys()
    {
        IUtilityService utilityService = new UtilityService();

        //
        // Check valid paths pass
        //

        utilityService.IsValidResourceKeySegment("ValidSegment").Should().BeTrue();
        utilityService.IsValidResourceKey(@"Some/Path/File.txt").Should().BeTrue();

        //
        // Check invalid paths fail
        //

        utilityService.IsValidResourceKeySegment("Invalid\"Segment").Should().BeFalse();
        utilityService.IsValidResourceKey(@"C:\\AbsolutePath").Should().BeFalse();
        utilityService.IsValidResourceKey(@"\AbsolutePath").Should().BeFalse();
        utilityService.IsValidResourceKey(@"/Some/Path/File.txt").Should().BeFalse();
    }
}
