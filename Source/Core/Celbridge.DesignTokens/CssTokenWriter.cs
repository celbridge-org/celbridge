using System.Globalization;
using System.Text;

namespace Celbridge.DesignTokens;

/// <summary>
/// Emits the token sheet served to WebView content: a light block holding every token and a dark block
/// holding those that change with the theme.
/// </summary>
public static class CssTokenWriter
{
    private const string DeclarationIndent = "    ";

    public static string Write(DesignTokenSource source)
    {
        var builder = new StringBuilder();

        foreach (var line in CommentFormatter.FormatBlock(source.CssHeader, string.Empty, "/* ", " */"))
        {
            builder.Append(line).Append('\n');
        }

        if (source.CssImports.Count > 0)
        {
            builder.Append('\n');

            foreach (var import in source.CssImports)
            {
                builder.Append($"@import url('{import}');\n");
            }
        }

        builder.Append('\n');
        AppendThemeBlock(builder, source, DesignTokenTheme.Light, ":root");

        builder.Append('\n');
        AppendThemeBlock(builder, source, DesignTokenTheme.Dark, ":root[data-theme=\"dark\"]");

        return builder.ToString();
    }

    private static void AppendThemeBlock(
        StringBuilder builder,
        DesignTokenSource source,
        DesignTokenTheme theme,
        string selector)
    {
        builder.Append($"{selector} {{\n");

        var isFirstGroup = true;

        foreach (var group in source.Groups)
        {
            var tokens = SelectTokens(group, theme);
            if (tokens.Count == 0)
            {
                continue;
            }

            if (!isFirstGroup)
            {
                builder.Append('\n');
            }

            isFirstGroup = false;

            // Group prose describes the tokens rather than one theme's values, so it is emitted once.
            if (theme == DesignTokenTheme.Light)
            {
                AppendComment(builder, group.Comment);
            }

            foreach (var token in tokens)
            {
                AppendDeclaration(builder, token, theme);
            }
        }

        builder.Append("}\n");
    }

    // The dark block overrides only what changes with the theme, so it skips theme invariant tokens.
    private static IReadOnlyList<DesignToken> SelectTokens(DesignTokenGroup group, DesignTokenTheme theme)
    {
        return group.Tokens
            .Where(token => token.EmitsCss)
            .Where(token => theme == DesignTokenTheme.Light || !token.IsThemeInvariant)
            .ToList();
    }

    private static void AppendDeclaration(StringBuilder builder, DesignToken token, DesignTokenTheme theme)
    {
        var comment = theme == DesignTokenTheme.Light ? token.Comment : token.DarkComment;
        AppendComment(builder, comment);

        var value = FormatValue(token.ValueForTheme(theme));
        builder.Append($"{DeclarationIndent}{token.CssPropertyName}: {value};\n");
    }

    // Values are held in the source as XAML hex so the two targets cannot express the same colour
    // differently. Eight digit hex carries alpha, which CSS spells as a colour function.
    private static string FormatValue(string value)
    {
        if (value.Length != 9 ||
            !value.StartsWith('#'))
        {
            return value;
        }

        var alpha = Convert.ToInt32(value.Substring(1, 2), 16);
        var red = Convert.ToInt32(value.Substring(3, 2), 16);
        var green = Convert.ToInt32(value.Substring(5, 2), 16);
        var blue = Convert.ToInt32(value.Substring(7, 2), 16);

        var opacity = Math.Round(alpha / 255.0, 3).ToString(CultureInfo.InvariantCulture);

        return $"rgba({red}, {green}, {blue}, {opacity})";
    }

    private static void AppendComment(StringBuilder builder, IReadOnlyList<string> paragraphs)
    {
        foreach (var line in CommentFormatter.FormatBlock(paragraphs, DeclarationIndent, "/* ", " */"))
        {
            builder.Append(line).Append('\n');
        }
    }
}
