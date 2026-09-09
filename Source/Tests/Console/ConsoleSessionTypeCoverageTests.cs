using System.Text.RegularExpressions;

namespace Celbridge.Tests.Console;

/// <summary>
/// The console settings form offers the session types the host reports as registered, but it can only edit
/// one it has authored fields for, so a type registered with no group in index.html never reaches the Type
/// dropdown. These tests pin the two sides together, so adding a session type provider without its fields
/// fails here rather than showing up as a type the user cannot select.
/// </summary>
[TestFixture]
public class ConsoleSessionTypeCoverageTests
{
    // The built-in session types, as their providers declare TypeId.
    private static readonly string[] RegisteredTypeIds = { "shell", "python" };

    private static readonly Regex TypeGroupRegex =
        new("class=\"type-fields[^\"]*\" data-type=\"([^\"]+)\"", RegexOptions.Compiled);

    private static readonly Regex SessionTypeOptionRegex =
        new("<select id=\"session-type\">\\s*<option", RegexOptions.Compiled);

    [Test]
    public void EveryRegisteredSessionType_HasFieldsInTheConsoleSettingsForm()
    {
        var html = File.ReadAllText(FindConsoleIndexHtml());

        var groupTypeIds = TypeGroupRegex.Matches(html)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        groupTypeIds.Should().BeEquivalentTo(RegisteredTypeIds);
    }

    [Test]
    public void TheSessionTypeDropdown_DeclaresNoOptionsOfItsOwn()
    {
        // The options are built from the types the host reports, so a hardcoded one would offer a type
        // nothing is registered for, or hide one that is.
        var html = File.ReadAllText(FindConsoleIndexHtml());

        SessionTypeOptionRegex.IsMatch(html).Should().BeFalse(
            "the Type options are built from the host's registered types, not authored in the markup");
    }

    private static string FindConsoleIndexHtml()
    {
        var indexHtmlPath = Directory
            .GetFiles(AppContext.BaseDirectory, "index.html", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "Console");

        indexHtmlPath.Should().NotBeNull("the Console web app should be copied to the test output");

        return indexHtmlPath!;
    }
}
