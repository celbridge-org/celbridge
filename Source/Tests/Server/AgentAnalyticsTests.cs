using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

[TestFixture]
public class AgentAnalyticsTests
{
    // ApproximateTokenCount — Anthropic's chars/4 rule of thumb

    [TestCase(0, ExpectedResult = 0)]
    [TestCase(1, ExpectedResult = 1)]
    [TestCase(3, ExpectedResult = 1)]
    [TestCase(4, ExpectedResult = 1)]
    [TestCase(5, ExpectedResult = 2)]
    [TestCase(8, ExpectedResult = 2)]
    [TestCase(100, ExpectedResult = 25)]
    public int ApproximateTokenCount_RoundsUpDivByFour(int characters)
    {
        return AgentAnalytics.ApproximateTokenCount(characters);
    }

    // ExtractNamespace — split on first underscore

    [TestCase("app_get_state", ExpectedResult = "app")]
    [TestCase("file_grep", ExpectedResult = "file")]
    [TestCase("spreadsheet_read_sheet", ExpectedResult = "spreadsheet")]
    [TestCase("guides_list", ExpectedResult = "guides")]
    public string ExtractNamespace_SplitsOnFirstUnderscore(string toolName)
    {
        return AgentAnalytics.ExtractNamespace(toolName);
    }

    [TestCase("noseparator", ExpectedResult = "noseparator")]
    [TestCase("", ExpectedResult = "")]
    [TestCase("_leading", ExpectedResult = "_leading")]
    public string ExtractNamespace_NoUnderscoreReturnsFullName(string toolName)
    {
        // Underscore at index 0 means there's no namespace prefix; bucket the
        // tool under its own name rather than producing an empty namespace.
        return AgentAnalytics.ExtractNamespace(toolName);
    }
}
