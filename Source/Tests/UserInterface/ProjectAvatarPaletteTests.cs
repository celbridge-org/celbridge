using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The avatar has to be stable and discriminating: the same project always draws the same initials and
/// colour, and projects that share initials still differ in colour. These cover the word forms project
/// names take, and the stability of the colour hash.
/// </summary>
[TestFixture]
public class ProjectAvatarPaletteTests
{
    [TestCase("demo", "D", TestName = "single lower case word")]
    [TestCase("celbridge_tdd", "CT", TestName = "underscore separated")]
    [TestCase("Test Examples", "TE", TestName = "space separated")]
    [TestCase("IntegrationTest", "IT", TestName = "camel case run")]
    [TestCase("test_project_1", "TP", TestName = "more words than initials")]
    [TestCase("phase6", "P", TestName = "trailing digits stay in the word")]
    public void Initials_AreTakenFromTheFirstTwoWords(string projectName, string expected)
    {
        ProjectAvatarPalette.GetInitials(projectName).Should().Be(expected);
    }

    [TestCase("")]
    [TestCase("___")]
    public void Initials_FallBackWhenTheNameHasNoLettersOrDigits(string projectName)
    {
        ProjectAvatarPalette.GetInitials(projectName).Should().Be("?");
    }

    [Test]
    public void Color_IsTheSameForTheSameName()
    {
        var first = ProjectAvatarPalette.GetTileColorHex("celbridge_tdd");
        var second = ProjectAvatarPalette.GetTileColorHex("celbridge_tdd");

        first.Should().Be(second);
    }

    [Test]
    public void Color_DiffersForNamesThatShareInitials()
    {
        // The initials of both are "TE", so only the colour tells the two rows apart.
        var testExamples = ProjectAvatarPalette.GetTileColorHex("Test Examples");
        var testEmpty = ProjectAvatarPalette.GetTileColorHex("TestEmpty");

        testExamples.Should().NotBe(testEmpty);
    }
}
