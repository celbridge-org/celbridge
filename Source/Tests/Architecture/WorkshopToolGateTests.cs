using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the workshop feature flag's coverage of the package_* and page_* tool namespaces. The flag is
/// off in shipping builds, so a tool that talks to the workshop without the guard would reach a server the
/// build has not opted into. Adding a workshop tool means adding the guard; adding a local-only one means
/// naming it below, so either way the decision is deliberate.
/// </summary>
[TestFixture]
public class WorkshopToolGateTests
{
    // The tool folders whose namespaces the workshop flag covers, relative to the Source folder.
    private static readonly string[] ToolFolders =
    {
        Path.Combine("Core", "Celbridge.Tools", "Tools", "Package"),
        Path.Combine("Core", "Celbridge.Tools", "Tools", "Page")
    };

    // Tools in those namespaces that never reach the workshop: zip and unzip inside the project tree, and
    // a report on what the project already has installed. They stay available in every build.
    private static readonly string[] LocalOnlyTools =
    {
        "package_archive",
        "package_status",
        "package_unarchive"
    };

    private static readonly Regex ToolNamePattern = new(
        @"\[McpServerTool\(Name = ""(?<name>[a-z_]+)""",
        RegexOptions.Compiled);

    private const string WorkshopGuard = "if (!IsWorkshopEnabled)";

    [Test]
    public void EveryWorkshopTool_IsGatedOnTheWorkshopFlag()
    {
        var ungated = new List<string>();

        foreach (var (toolName, source) in EnumerateTools())
        {
            if (LocalOnlyTools.Contains(toolName))
            {
                continue;
            }

            if (!source.Contains(WorkshopGuard, StringComparison.Ordinal))
            {
                ungated.Add(toolName);
            }
        }

        ungated.Should().BeEmpty(
            "every package_* and page_* tool that reaches the workshop must return FeatureFlagDisabled when the flag is off");
    }

    [Test]
    public void LocalOnlyTools_AreNotGated()
    {
        var gated = new List<string>();

        foreach (var (toolName, source) in EnumerateTools())
        {
            if (LocalOnlyTools.Contains(toolName) && source.Contains(WorkshopGuard, StringComparison.Ordinal))
            {
                gated.Add(toolName);
            }
        }

        gated.Should().BeEmpty("a tool that never reaches the workshop should not be withheld by the workshop flag");
    }

    [Test]
    public void LocalOnlyTools_AllExist()
    {
        var toolNames = EnumerateTools().Select(tool => tool.ToolName).ToList();

        toolNames.Should().Contain(LocalOnlyTools, "a stale exemption would silently excuse a tool that no longer exists");
    }

    private static IReadOnlyList<(string ToolName, string Source)> EnumerateTools()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty();

        var tools = new List<(string, string)>();

        foreach (var relativeFolder in ToolFolders)
        {
            var folder = Path.Combine(sourceFolder, relativeFolder);
            Directory.Exists(folder).Should().BeTrue($"tool folder not found: {folder}");

            foreach (var filePath in Directory.EnumerateFiles(folder, "*.cs"))
            {
                var source = ArchitectureHelpers.ReadSourceFile(filePath);

                foreach (Match match in ToolNamePattern.Matches(source))
                {
                    tools.Add((match.Groups["name"].Value, source));
                }
            }
        }

        tools.Should().NotBeEmpty();

        return tools;
    }
}
