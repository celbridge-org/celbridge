using System.Reflection;
using Celbridge.Tools;
using ModelContextProtocol.Server;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Keeps the workshop feature flag's coverage of the package_* and page_* namespaces honest. The flag
/// is off in shipping builds, so a tool that reaches the workshop without the [WorkshopTool] marker
/// would be offered and callable in a build that never opted in. Adding a workshop tool means adding
/// the marker; adding a local-only one means naming it below, so either way the decision is deliberate.
/// </summary>
[TestFixture]
public class WorkshopToolGateTests
{
    // The namespaces the workshop flag covers, by MCP tool-name prefix.
    private static readonly string[] GatedNamespacePrefixes = { "package_", "page_" };

    // Tools in those namespaces that never reach the workshop: zip and unzip inside the project tree,
    // and a report on what the project already has installed. They stay available in every build.
    private static readonly string[] LocalOnlyTools =
    {
        "package_archive",
        "package_status",
        "package_unarchive"
    };

    [Test]
    public void EveryWorkshopTool_CarriesTheMarker()
    {
        var ungated = DiscoverTools()
            .Where(tool => !LocalOnlyTools.Contains(tool.ToolName) && !tool.IsWorkshopTool)
            .Select(tool => tool.ToolName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        ungated.Should().BeEmpty(
            "a package_* or page_* tool that reaches the workshop must carry [WorkshopTool] so it is withheld when the flag is off");
    }

    [Test]
    public void LocalOnlyTools_DoNotCarryTheMarker()
    {
        var gated = DiscoverTools()
            .Where(tool => LocalOnlyTools.Contains(tool.ToolName) && tool.IsWorkshopTool)
            .Select(tool => tool.ToolName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        gated.Should().BeEmpty("a tool that never reaches the workshop should not be withheld by the workshop flag");
    }

    [Test]
    public void LocalOnlyTools_AllExist()
    {
        var toolNames = DiscoverTools().Select(tool => tool.ToolName).ToList();

        toolNames.Should().Contain(LocalOnlyTools, "a stale exemption would silently excuse a tool that no longer exists");
    }

    [Test]
    public void TheMarker_IsOnlyUsedInTheGatedNamespaces()
    {
        var strays = DiscoverAllTools()
            .Where(tool => tool.IsWorkshopTool && !GatedNamespacePrefixes.Any(prefix => tool.ToolName.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(tool => tool.ToolName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        strays.Should().BeEmpty("the workshop flag is documented as covering package_* and page_* only");
    }

    private record ToolMarker(string ToolName, bool IsWorkshopTool);

    private static IReadOnlyList<ToolMarker> DiscoverTools()
    {
        var tools = DiscoverAllTools()
            .Where(tool => GatedNamespacePrefixes.Any(prefix => tool.ToolName.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        tools.Should().NotBeEmpty("the package_* and page_* tools should be discoverable by reflection");

        return tools;
    }

    private static IReadOnlyList<ToolMarker> DiscoverAllTools()
    {
        var tools = new List<ToolMarker>();

        foreach (var type in typeof(AppTools).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var toolName = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
                if (string.IsNullOrEmpty(toolName))
                {
                    continue;
                }

                tools.Add(new ToolMarker(toolName, method.GetCustomAttribute<WorkshopToolAttribute>() is not null));
            }
        }

        return tools;
    }
}
