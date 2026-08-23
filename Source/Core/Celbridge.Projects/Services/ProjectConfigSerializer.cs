using System.Globalization;
using System.Text;

namespace Celbridge.Projects.Services;

/// <summary>
/// Serializes a project config to canonical, deterministic TOML. The same resolved model always
/// produces the same bytes: a fixed section and key order, uniform inline arrays and tables, and
/// config keys sorted per contribution.
/// </summary>
public static class ProjectConfigSerializer
{
    public static string Serialize(ProjectConfig config)
    {
        var builder = new StringBuilder();

        WriteCelbridgeTable(builder, config);
        WriteResourcesTable(builder, config.Resources);
        WriteContributions(builder, config.ContributionOverrides);

        return builder.ToString();
    }

    private static void WriteCelbridgeTable(StringBuilder builder, ProjectConfig config)
    {
        builder.Append("[celbridge]\n");

        var celbridge = config.Celbridge;
        if (!string.IsNullOrEmpty(celbridge.CelbridgeVersion))
        {
            WriteKeyValue(builder, "celbridge-version", RenderString(celbridge.CelbridgeVersion));
        }
        if (!string.IsNullOrEmpty(celbridge.ProjectVersion))
        {
            WriteKeyValue(builder, "project-version", RenderString(celbridge.ProjectVersion));
        }
        if (!string.IsNullOrEmpty(celbridge.Description))
        {
            WriteKeyValue(builder, "description", RenderString(celbridge.Description));
        }
        if (!string.IsNullOrEmpty(celbridge.DataFolder))
        {
            WriteKeyValue(builder, "data-folder", RenderString(celbridge.DataFolder));
        }
        if (celbridge.EditorAssociations.Count > 0)
        {
            WriteKeyValue(builder, "editor-associations", RenderInlineTable(celbridge.EditorAssociations));
        }
        if (celbridge.DisabledPackages.Count > 0)
        {
            WriteKeyValue(builder, "disabled-packages", RenderStringArray(celbridge.DisabledPackages));
        }
        if (config.Features.Count > 0)
        {
            WriteKeyValue(builder, "features", RenderBoolInlineTable(config.Features));
        }
    }

    private static void WriteResourcesTable(StringBuilder builder, ResourcesSection resources)
    {
        builder.Append('\n');
        builder.Append("# The resource set: the files the ignore-file allows, plus 'add', minus 'remove'.\n");
        builder.Append("# 'lock' freezes resources so they can't be edited, moved, or deleted.\n");
        builder.Append("[celbridge.resources]\n");
        WriteKeyValue(builder, "ignore-file", RenderString(resources.IgnoreFile));
        WriteKeyValue(builder, "add", RenderStringArray(resources.Add));
        WriteKeyValue(builder, "remove", RenderStringArray(resources.Remove));
        WriteKeyValue(builder, "lock", RenderStringArray(resources.Lock));
    }

    // Emits the [[contribution]] override entries, sorted by package then contribution so the same
    // resolved overrides always serialize to the same bytes. Within an entry: identity keys, then the
    // activation flip, then config in key order.
    private static void WriteContributions(StringBuilder builder, IReadOnlyList<ContributionOverride> contributions)
    {
        var ordered = contributions
            .OrderBy(contribution => contribution.PackageName, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal);

        foreach (var contribution in ordered)
        {
            builder.Append('\n');
            builder.Append("[[contribution]]\n");

            WriteKeyValue(builder, ContributionPropertyKeys.Package, RenderString(contribution.PackageName));
            WriteKeyValue(builder, ContributionPropertyKeys.Contribution, RenderString(contribution.ContributionId));

            if (contribution.Disabled)
            {
                WriteKeyValue(builder, ContributionPropertyKeys.Disabled, "true");
            }
            if (contribution.Enabled)
            {
                WriteKeyValue(builder, ContributionPropertyKeys.Enabled, "true");
            }

            foreach (var key in contribution.Config.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                WriteKeyValue(builder, key, RenderConfigValue(contribution.Config[key]));
            }
        }
    }

    private static void WriteKeyValue(StringBuilder builder, string key, string renderedValue)
    {
        builder.Append(key).Append(" = ").Append(renderedValue).Append('\n');
    }

    private static string RenderConfigValue(object? value)
    {
        switch (value)
        {
            case bool boolValue:
                return boolValue ? "true" : "false";

            case long longValue:
                return longValue.ToString(CultureInfo.InvariantCulture);

            case double doubleValue:
                return doubleValue.ToString(CultureInfo.InvariantCulture);

            case string stringValue:
                return RenderString(stringValue);

            case IReadOnlyList<string> listValue:
                return RenderStringArray(listValue);

            default:
                return RenderString(value?.ToString() ?? string.Empty);
        }
    }

    private static string RenderStringArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        var items = values.Select(RenderString);
        return $"[{string.Join(", ", items)}]";
    }

    private static string RenderInlineTable(IReadOnlyDictionary<string, string> map)
    {
        var entries = map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{RenderString(pair.Key)} = {RenderString(pair.Value)}");
        return $"{{ {string.Join(", ", entries)} }}";
    }

    private static string RenderBoolInlineTable(IReadOnlyDictionary<string, bool> map)
    {
        var entries = map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{RenderString(pair.Key)} = {(pair.Value ? "true" : "false")}");
        return $"{{ {string.Join(", ", entries)} }}";
    }

    // Renders a TOML basic string with the standard escapes.
    private static string RenderString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    // TOML basic strings forbid unescaped control characters. The common ones are
                    // handled above. Escape the rest (and DEL) as \uXXXX so the file re-parses.
                    if (character < ' '
                        || character == '\u007f')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');

        return builder.ToString();
    }
}
