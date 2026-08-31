using System.Globalization;
using System.Text;
using Celbridge.Workspace;

namespace Celbridge.Projects;

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
        WriteDocumentShortcuts(builder, config.DocumentShortcuts);

        return builder.ToString();
    }

    private static void WriteCelbridgeTable(StringBuilder builder, ProjectConfig config)
    {
        builder.Append("[celbridge]\n");

        var celbridge = config.Celbridge;
        if (!string.IsNullOrEmpty(celbridge.CelbridgeVersion))
        {
            WriteKeyValue(builder, "celbridge-version", TomlStringEncoder.EncodeBasicString(celbridge.CelbridgeVersion));
        }
        if (!string.IsNullOrEmpty(celbridge.ProjectVersion))
        {
            WriteKeyValue(builder, "project-version", TomlStringEncoder.EncodeBasicString(celbridge.ProjectVersion));
        }
        if (!string.IsNullOrEmpty(celbridge.Description))
        {
            WriteKeyValue(builder, "description", TomlStringEncoder.EncodeBasicString(celbridge.Description));
        }
        if (!string.IsNullOrEmpty(celbridge.DataFolder))
        {
            WriteKeyValue(builder, "data-folder", TomlStringEncoder.EncodeBasicString(celbridge.DataFolder));
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
        WriteKeyValue(builder, "ignore-file", TomlStringEncoder.EncodeBasicString(resources.IgnoreFile));
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

            WriteKeyValue(builder, ContributionPropertyKeys.Package, TomlStringEncoder.EncodeBasicString(contribution.PackageName));
            WriteKeyValue(builder, ContributionPropertyKeys.Contribution, TomlStringEncoder.EncodeBasicString(contribution.ContributionId));

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

    // Emits the [[shortcut]] entries in list order. Unlike the contribution entries these are not sorted:
    // the order is the rail order the user arranged, so it is part of the value rather than an artifact of
    // how the entries were collected.
    private static void WriteDocumentShortcuts(StringBuilder builder, IReadOnlyList<DocumentShortcut> documentShortcuts)
    {
        foreach (var documentShortcut in documentShortcuts)
        {
            builder.Append('\n');
            builder.Append("[[shortcut]]\n");

            WriteKeyValue(builder, "resource", TomlStringEncoder.EncodeBasicString(documentShortcut.Resource));

            if (!string.IsNullOrEmpty(documentShortcut.Icon))
            {
                WriteKeyValue(builder, "icon", TomlStringEncoder.EncodeBasicString(documentShortcut.Icon));
            }

            if (documentShortcut.Area != WorkspaceArea.Main)
            {
                WriteKeyValue(builder, "area", TomlStringEncoder.EncodeBasicString(documentShortcut.Area.ToToken()));
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
                return TomlStringEncoder.EncodeBasicString(stringValue);

            case IReadOnlyList<string> listValue:
                return RenderStringArray(listValue);

            default:
                return TomlStringEncoder.EncodeBasicString(value?.ToString() ?? string.Empty);
        }
    }

    private static string RenderStringArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        var items = values.Select(TomlStringEncoder.EncodeBasicString);
        return $"[{string.Join(", ", items)}]";
    }

    private static string RenderInlineTable(IReadOnlyDictionary<string, string> map)
    {
        var entries = map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{TomlStringEncoder.EncodeBasicString(pair.Key)} = {TomlStringEncoder.EncodeBasicString(pair.Value)}");
        return $"{{ {string.Join(", ", entries)} }}";
    }

    private static string RenderBoolInlineTable(IReadOnlyDictionary<string, bool> map)
    {
        var entries = map
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{TomlStringEncoder.EncodeBasicString(pair.Key)} = {(pair.Value ? "true" : "false")}");
        return $"{{ {string.Join(", ", entries)} }}";
    }
}
