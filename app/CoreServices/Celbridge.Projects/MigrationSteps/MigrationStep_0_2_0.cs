using Celbridge.Projects.Services;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn.Model;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Migrates the shortcuts configuration from the legacy nested table format to the new flat array format.
/// 
/// Legacy format (pre-0.2.0):
/// [shortcuts.navigation_bar.run_examples]
/// icon="Play"
/// tooltip="Run examples"
/// [shortcuts.navigation_bar.run_examples.hello_world]
/// script='run "hello.py"'
/// 
/// New format (0.2.0+):
/// [[shortcut]]
/// name = "Run Examples"
/// icon = "Play"
/// tooltip = "Run examples"
/// 
/// [[shortcut]]
/// name = "Run Examples/Hello World"
/// script = 'run "hello.py"'
/// </summary>
public class MigrationStep_0_2_0 : IMigrationStep
{
    public Version TargetVersion => new Version("0.2.0");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        // Check if there's a legacy [shortcuts] section with navigation_bar
        if (!context.Configuration.TryGetValue("shortcuts", out var shortcutsObj))
        {
            // No shortcuts section at all - nothing to migrate
            return Result.Ok();
        }

        // If shortcut is already a TomlTableArray, it's already in the new format
        if (shortcutsObj is TomlTableArray)
        {
            context.Logger.LogInformation("Shortcuts already in new array format, skipping migration");
            return Result.Ok();
        }

        // If shortcuts is a TomlTable, check for navigation_bar
        if (shortcutsObj is not TomlTable shortcutsTable)
        {
            return Result.Ok();
        }

        if (!shortcutsTable.TryGetValue("navigation_bar", out var navigationBarObj) ||
            navigationBarObj is not TomlTable navigationBarTable)
        {
            // No navigation_bar section - nothing to migrate
            return Result.Ok();
        }

        context.Logger.LogInformation("Converting legacy shortcuts format to new [[shortcut]] array format");

        // Extract shortcuts from the legacy format
        var shortcuts = new List<ShortcutEntry>();
        ExtractShortcutsRecursively(navigationBarTable, shortcuts, null);

        if (shortcuts.Count == 0)
        {
            context.Logger.LogInformation("No shortcuts found in legacy format");
            return Result.Ok();
        }

        // Read the original file
        var originalText = await File.ReadAllTextAsync(context.ProjectFilePath);

        // Remove the old [shortcuts...] sections
        var updatedText = RemoveLegacyShortcutsSections(originalText);

        // Generate new [[shortcut]] array entries
        var newShortcutsText = GenerateNewShortcutFormat(shortcuts);

        // Append the new shortcuts to the end of the file
        updatedText = updatedText.TrimEnd() + "\n\n" + newShortcutsText;

        // Write the updated file
        var writeResult = await context.WriteProjectFileAsync(updatedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail("Failed to write migrated shortcuts configuration")
                .WithErrors(writeResult);
        }

        context.Logger.LogInformation($"Successfully migrated {shortcuts.Count} shortcuts to new format");
        return Result.Ok();
    }

    private record ShortcutEntry(
        string FullName,  // Full path including hierarchy, e.g., "Run Examples/Hello World"
        string? Icon,
        string? Tooltip,
        string? Script);

    /// <summary>
    /// Recursively extract shortcuts from the legacy nested table format.
    /// </summary>
    private void ExtractShortcutsRecursively(
        TomlTable table,
        List<ShortcutEntry> shortcuts,
        string? parentPath)
    {
        // First pass: identify child tables (groups or commands)
        var childTables = new Dictionary<string, TomlTable>();
        foreach (var (key, value) in table)
        {
            if (value is TomlTable childTable)
            {
                childTables[key] = childTable;
            }
        }

        // Process child tables
        foreach (var (childKey, childTable) in childTables)
        {
            // Humanize the key to get a display name
            var childDisplayName = HumanizeName(childKey);
            
            // Check if this child has its own children (making it a group)
            bool hasChildTables = childTable.Keys.Any(k => childTable[k] is TomlTable);
            
            // Extract child properties
            string? childIcon = childTable.TryGetValue("icon", out var childIconObj) ? childIconObj?.ToString() : null;
            string? childTooltip = childTable.TryGetValue("tooltip", out var childTooltipObj) ? childTooltipObj?.ToString() : null;
            string? childScript = childTable.TryGetValue("script", out var childScriptObj) ? childScriptObj?.ToString() : null;
            string? childNameOverride = childTable.TryGetValue("name", out var childNameObj) ? childNameObj?.ToString() : null;
            
            if (!string.IsNullOrEmpty(childNameOverride))
            {
                childDisplayName = childNameOverride;
            }

            // Build the full name (path) for this item
            var fullName = string.IsNullOrEmpty(parentPath) 
                ? childDisplayName 
                : $"{parentPath}/{childDisplayName}";

            if (hasChildTables)
            {
                // This is a group - add it as a group entry (no script)
                shortcuts.Add(new ShortcutEntry(
                    FullName: fullName,
                    Icon: childIcon,
                    Tooltip: childTooltip,
                    Script: null));

                // Recursively process children
                ExtractShortcutsRecursively(childTable, shortcuts, fullName);
            }
            else
            {
                // This is a leaf command
                shortcuts.Add(new ShortcutEntry(
                    FullName: fullName,
                    Icon: childIcon,
                    Tooltip: childTooltip,
                    Script: childScript));
            }
        }
    }

    /// <summary>
    /// Convert underscore_case to Title Case.
    /// </summary>
    private string HumanizeName(string name)
    {
        // Split by underscore and capitalize each word
        var words = name.Split('_')
            .Where(w => !string.IsNullOrEmpty(w))
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower())
            .ToArray();
        return string.Join(" ", words);
    }

    /// <summary>
    /// Remove all legacy [shortcuts...] sections from the file content.
    /// </summary>
    private string RemoveLegacyShortcutsSections(string content)
    {
        // Match [shortcuts] or [shortcuts.anything] sections and their content
        // This regex matches from the section header to the next section header or end of file
        var pattern = @"^\s*\[shortcuts(?:\.[^\]]+)?\]\s*\r?\n(?:(?!\[)[^\r\n]*\r?\n)*";
        
        var result = Regex.Replace(content, pattern, "", RegexOptions.Multiline);
        
        // Also remove any standalone # Shortcut comments that might be left
        result = Regex.Replace(result, @"^#\s*[Ss]hortcut[^\r\n]*\r?\n", "", RegexOptions.Multiline);
        
        // Clean up multiple consecutive blank lines
        result = Regex.Replace(result, @"(\r?\n){3,}", "\n\n");
        
        return result;
    }

    /// <summary>
    /// Generate the new [[shortcut]] array format from the extracted shortcuts.
    /// </summary>
    private string GenerateNewShortcutFormat(List<ShortcutEntry> shortcuts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Shortcut buttons");
        sb.AppendLine();

        foreach (var shortcut in shortcuts)
        {
            sb.AppendLine("[[shortcut]]");
            sb.AppendLine($"name = \"{EscapeTomlString(shortcut.FullName)}\"");
            
            if (!string.IsNullOrEmpty(shortcut.Icon))
            {
                sb.AppendLine($"icon = \"{EscapeTomlString(shortcut.Icon)}\"");
            }
            
            if (!string.IsNullOrEmpty(shortcut.Tooltip))
            {
                sb.AppendLine($"tooltip = \"{EscapeTomlString(shortcut.Tooltip)}\"");
            }
            
            if (!string.IsNullOrEmpty(shortcut.Script))
            {
                // Use multiline string for scripts that contain newlines or quotes
                if (shortcut.Script.Contains('\n') || shortcut.Script.Contains('"'))
                {
                    sb.AppendLine($"script = '''");
                    sb.AppendLine(shortcut.Script);
                    sb.AppendLine("'''");
                }
                else
                {
                    sb.AppendLine($"script = \"{EscapeTomlString(shortcut.Script)}\"");
                }
            }
            
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>
    /// Escape special characters in a TOML string value.
    /// </summary>
    private string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\t", "\\t")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
