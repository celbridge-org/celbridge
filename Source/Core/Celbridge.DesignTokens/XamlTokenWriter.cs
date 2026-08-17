using System.Text;

namespace Celbridge.DesignTokens;

/// <summary>
/// Emits the theme dictionaries, each holding its own colors and the brushes over them, as a WinUI resource
/// dictionary. The WinUI system brush overrides are not emitted: they decide which native control keys
/// redirect onto the palette, so they stay hand written in the dictionary that merges this one.
/// </summary>
public static class XamlTokenWriter
{
    private const string ThemeDictionaryIndent = "      ";

    // Emitted into the generated dictionary so a reader there is told why each brush is declared per theme
    // rather than once over the palette.
    private static readonly IReadOnlyList<string> BrushPlacementComment = new List<string>
    {
        "Each brush is declared in both theme dictionaries rather than once over the colors. A brush declared over them is one shared object whose color resolves against the application theme, which follows the operating system, so it would ignore an element asking for it under the opposite ElementTheme."
    };

    public static string Write(DesignTokenSource source)
    {
        var builder = new StringBuilder();

        foreach (var line in CommentFormatter.FormatBlock(source.XamlHeader, string.Empty, "<!-- ", " -->"))
        {
            builder.Append(line).Append('\n');
        }

        builder.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n");
        builder.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");
        builder.Append('\n');
        builder.Append("  <ResourceDictionary.ThemeDictionaries>\n");

        AppendThemeDictionary(builder, source, DesignTokenTheme.Light);
        AppendThemeDictionary(builder, source, DesignTokenTheme.Dark);

        builder.Append('\n');
        builder.Append("  </ResourceDictionary.ThemeDictionaries>\n");

        builder.Append('\n');
        builder.Append("</ResourceDictionary>\n");

        return builder.ToString();
    }

    private static void AppendThemeDictionary(StringBuilder builder, DesignTokenSource source, DesignTokenTheme theme)
    {
        builder.Append('\n');
        builder.Append($"    <ResourceDictionary x:Key=\"{theme}\">\n");

        var isFirstGroup = true;

        foreach (var group in source.Groups)
        {
            var tokens = group.Tokens.Where(token => token.EmitsXaml).ToList();
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
                AppendComment(builder, group.Comment, ThemeDictionaryIndent);
            }

            foreach (var token in tokens)
            {
                AppendColor(builder, token, theme);
            }
        }

        AppendBrushes(builder, source, theme);

        builder.Append("    </ResourceDictionary>\n");
    }

    private static void AppendColor(StringBuilder builder, DesignToken token, DesignTokenTheme theme)
    {
        var comment = theme == DesignTokenTheme.Light ? token.Comment : token.DarkComment;
        AppendComment(builder, comment, ThemeDictionaryIndent);

        var value = token.ValueForTheme(theme);
        builder.Append($"{ThemeDictionaryIndent}<Color x:Key=\"{token.XamlColorKey}\">{value}</Color>");

        // The web counterpart is named alongside the colour so a reader of this dictionary can find the
        // token on the other side without going back to the source file.
        if (token.EmitsCss)
        {
            var cssName = CommentFormatter.EscapeForXml(token.CssPropertyName!);
            builder.Append($"  <!-- {cssName} -->");
        }

        builder.Append('\n');
    }

    // A brush belongs to a theme, not to the palette as a whole. Declared once over the colors it would be a
    // single shared object whose color resolves against the application theme, which follows the operating
    // system, so an element asking for it under the opposite ElementTheme would still be handed the other
    // theme's color. One brush per theme dictionary is what makes a brush key answer to the element's theme.
    private static void AppendBrushes(StringBuilder builder, DesignTokenSource source, DesignTokenTheme theme)
    {
        var brushTokens = source.Tokens
            .Where(token => token.XamlBrushKey is not null)
            .ToList();

        if (brushTokens.Count == 0)
        {
            return;
        }

        builder.Append('\n');

        if (theme == DesignTokenTheme.Light)
        {
            AppendComment(builder, BrushPlacementComment, ThemeDictionaryIndent);
        }

        foreach (var token in brushTokens)
        {
            // The color sits in this same dictionary and above this line, so the brush takes its own theme's
            // value rather than asking the theme system a second time.
            builder.Append($"{ThemeDictionaryIndent}<SolidColorBrush x:Key=\"{token.XamlBrushKey}\"\n");
            builder.Append($"{ThemeDictionaryIndent}                 Color=\"{{StaticResource {token.XamlColorKey}}}\" />\n");
        }
    }

    private static void AppendComment(StringBuilder builder, IReadOnlyList<string> paragraphs, string indent)
    {
        var escaped = paragraphs.Select(CommentFormatter.EscapeForXml).ToList();

        foreach (var line in CommentFormatter.FormatBlock(escaped, indent, "<!-- ", " -->"))
        {
            builder.Append(line).Append('\n');
        }
    }
}
