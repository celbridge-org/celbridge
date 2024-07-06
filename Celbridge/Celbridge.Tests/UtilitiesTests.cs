using Celbridge.BaseLibrary.Utilities;
using Celbridge.Messaging.Services;

namespace Celbridge.Tests;

[TestFixture]
public class UtilitiesTests
{
    [Test]
    public void ICanValidatePathSegments()
    {
        IUtilityService utilityService = new UtilityService();

        //
        // Check valid paths pass
        //

        utilityService.IsValidPathSegment("ValidSegment").Should().BeTrue();
        utilityService.IsValidResourcePath(@"Some/Path/File.txt").Should().BeTrue();

        //
        // Check invalid paths fail
        //

        utilityService.IsValidPathSegment("Invalid\"Segment").Should().BeFalse();
        utilityService.IsValidResourcePath(@"C:\\AbsolutePath").Should().BeFalse();
        utilityService.IsValidResourcePath(@"\AbsolutePath").Should().BeFalse();
        utilityService.IsValidResourcePath(@"/Some/Path/File.txt").Should().BeFalse();
    }
}
