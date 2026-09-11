using Celbridge.Server.Services;
using Celbridge.Settings;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Server;

/// <summary>
/// Covers the single gate that withholds the workshop tools while the feature flag is off. Both an
/// agent on the MCP endpoint and a package WebView routed through McpToolBridge pass through here,
/// so these cases stand in for both callers.
/// </summary>
[TestFixture]
public class WorkshopToolFilterTests
{
    [Test]
    public void ToolNames_CoverTheWorkshopTools()
    {
        WorkshopToolFilter.ToolNames.Should().Contain("package_install");
        WorkshopToolFilter.ToolNames.Should().Contain("package_publish");
        WorkshopToolFilter.ToolNames.Should().Contain("page_publish");
    }

    [Test]
    public void ToolNames_ExcludeTheLocalTools()
    {
        WorkshopToolFilter.ToolNames.Should().NotContain("package_status");
        WorkshopToolFilter.ToolNames.Should().NotContain("package_archive");
        WorkshopToolFilter.ToolNames.Should().NotContain("package_unarchive");
    }

    [Test]
    public async Task ListFilter_FlagDisabled_DropsTheWorkshopTools()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: false));
        var result = await InvokeListAsync(filter, "package_install", "package_status", "file_read");

        result.Tools.Select(tool => tool.Name).Should().BeEquivalentTo("package_status", "file_read");
    }

    [Test]
    public async Task ListFilter_FlagEnabled_KeepsEveryTool()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: true));
        var result = await InvokeListAsync(filter, "package_install", "package_status", "file_read");

        result.Tools.Select(tool => tool.Name).Should().BeEquivalentTo("package_install", "package_status", "file_read");
    }

    [Test]
    public void ShouldWithhold_FlagDisabled_HoldsBackAWorkshopTool()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: false));

        filter.ShouldWithhold("package_install").Should().BeTrue();
        filter.ShouldWithhold("page_publish").Should().BeTrue();
    }

    [Test]
    public void ShouldWithhold_FlagDisabled_LetsLocalToolsThrough()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: false));

        filter.ShouldWithhold("package_status").Should().BeFalse();
        filter.ShouldWithhold("package_archive").Should().BeFalse();
        filter.ShouldWithhold("file_read").Should().BeFalse();
    }

    [Test]
    public void ShouldWithhold_FlagEnabled_LetsWorkshopToolsThrough()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: true));

        filter.ShouldWithhold("package_install").Should().BeFalse();
    }

    [Test]
    public void ShouldWithhold_MissingToolName_IsNotWithheld()
    {
        var filter = new WorkshopToolFilter(new StubFeatureFlags(workshopEnabled: false));

        filter.ShouldWithhold(null).Should().BeFalse();
        filter.ShouldWithhold(string.Empty).Should().BeFalse();
    }

    [Test]
    public void FeatureFlagDisabled_NamesTheWorkshopFlag()
    {
        var result = ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.Workshop);

        result.IsError.Should().BeTrue();
        ResultText(result).Should().Contain(FeatureFlagConstants.Workshop);
    }

    private static async Task<ListToolsResult> InvokeListAsync(WorkshopToolFilter filter, params string[] toolNames)
    {
        var listed = new ListToolsResult
        {
            Tools = toolNames.Select(name => new Tool { Name = name }).ToList()
        };

        var pipeline = filter.CreateListFilter()((_, _) => ValueTask.FromResult(listed));

        return await pipeline(null!, CancellationToken.None);
    }

    private static string ResultText(CallToolResult result)
    {
        return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    private sealed class StubFeatureFlags : IFeatureFlags
    {
        private readonly bool _workshopEnabled;

        public StubFeatureFlags(bool workshopEnabled)
        {
            _workshopEnabled = workshopEnabled;
        }

        public bool IsEnabled(string featureName)
        {
            return featureName == FeatureFlagConstants.Workshop ? _workshopEnabled : true;
        }

        public bool GetApplicationValue(string featureName) => IsEnabled(featureName);
        public void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides) { }
        public void ClearProjectOverrides() { }
    }
}
