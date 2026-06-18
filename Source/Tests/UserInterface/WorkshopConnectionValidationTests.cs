using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Workshop connection save-time validation rules.
/// </summary>
[TestFixture]
public class WorkshopConnectionValidationTests
{
    [TestCase("https://workshop.celbridge.org")]
    [TestCase("https://workshop.celbridge.org/api/v1")]
    [TestCase("http://localhost:5000")]
    [TestCase("http://127.0.0.1:5000")]
    [TestCase("  https://workshop.celbridge.org  ")]
    public void IsValidWorkshopUrl_AcceptsHttpsAndLoopbackHttp(string workshopUrl)
    {
        WorkshopConnectionValidation.IsValidWorkshopUrl(workshopUrl).Should().BeTrue();
    }

    [TestCase("http://workshop.celbridge.org")]
    [TestCase("ftp://workshop.celbridge.org")]
    [TestCase("workshop.celbridge.org")]
    [TestCase("not a url")]
    [TestCase("")]
    public void IsValidWorkshopUrl_RejectsOtherUrls(string workshopUrl)
    {
        WorkshopConnectionValidation.IsValidWorkshopUrl(workshopUrl).Should().BeFalse();
    }
}
