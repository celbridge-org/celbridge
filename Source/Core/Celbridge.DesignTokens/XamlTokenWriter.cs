using System.Text;

namespace Celbridge.DesignTokens;

/// <summary>
/// Emits the theme dictionaries, each holding its own colors, the brushes over them, and the WinUI control
/// keys redirected onto those colors, as a WinUI resource dictionary. A redirect belongs here rather than in
/// the hand written dictionary that merges this one because it has to be declared once per theme, and only
/// here is the color it points at in scope.
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

    // Emitted above the redirected colour keys.
    private static readonly IReadOnlyList<string> ColorAliasPlacementComment = new List<string>
    {
        "WinUI colour keys redirected onto the palette. The accent ramp is redirected at the color rather than at the brushes WinUI builds over it, because those brushes are aliased onward with StaticResource, which binds to the brush object rather than to the key. Only replacing the color underneath reaches them.",
        "Each step of the ramp holds the palette's accent rather than a shade of it. WinUI reads a different step per theme, and the palette already carries a value chosen for each theme, so the steps collapse onto it."
    };

    // Emitted above the redirected control keys, which are subject to the same rule as the brushes above.
    private static readonly IReadOnlyList<string> AliasPlacementComment = new List<string>
    {
        "WinUI's own control keys, redirected onto the palette so a native control paints from the same color as the chrome around it. Which key takes which color is a decision about the control rather than a value, so the list is held beside the color in the token source.",
        "A control key left out keeps its WinUI default, which on the accent keys means the color the operating system supplies."
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

        AppendColorKeyAliases(builder, source, theme);
        AppendBrushes(builder, source, theme);
        AppendControlKeyAliases(builder, source, theme);

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

        if (!TryOpenBlock(builder, brushTokens.Count, theme, BrushPlacementComment))
        {
            return;
        }

        foreach (var token in brushTokens)
        {
            AppendBrush(builder, token.XamlBrushKey!, token.XamlColorKey!);
        }
    }

    // Redirecting a colour rather than a brush is what reaches a control WinUI styles through its own
    // StaticResource alias: that alias binds to the brush object generic.xaml built, so replacing the brush
    // key leaves it pointing at the original, while replacing the colour the original reads changes it.
    private static void AppendColorKeyAliases(StringBuilder builder, DesignTokenSource source, DesignTokenTheme theme)
    {
        var aliasTokens = source.Tokens
            .Where(token => token.XamlColorAliases.Count > 0)
            .ToList();

        if (!TryOpenBlock(builder, aliasTokens.Count, theme, ColorAliasPlacementComment))
        {
            return;
        }

        foreach (var token in aliasTokens)
        {
            var value = token.ValueForTheme(theme);

            foreach (var alias in token.XamlColorAliases)
            {
                builder.Append($"{ThemeDictionaryIndent}<Color x:Key=\"{alias}\">{value}</Color>\n");
            }
        }
    }

    private static void AppendControlKeyAliases(StringBuilder builder, DesignTokenSource source, DesignTokenTheme theme)
    {
        var aliasTokens = source.Tokens
            .Where(token => token.XamlAliases.Count > 0)
            .ToList();

        if (!TryOpenBlock(builder, aliasTokens.Count, theme, AliasPlacementComment))
        {
            return;
        }

        foreach (var token in aliasTokens)
        {
            foreach (var alias in token.XamlAliases)
            {
                AppendBrush(builder, alias, token.XamlColorKey!);
            }
        }
    }

    // Separates a run of declarations from the one above it and heads the run with its prose, which describes
    // the tokens rather than one theme's values and so is emitted in the light dictionary only. False when the
    // run is empty and the caller has nothing to write.
    private static bool TryOpenBlock(StringBuilder builder, int entryCount, DesignTokenTheme theme, IReadOnlyList<string> comment)
    {
        if (entryCount == 0)
        {
            return false;
        }

        builder.Append('\n');

        if (theme == DesignTokenTheme.Light)
        {
            AppendComment(builder, comment, ThemeDictionaryIndent);
        }

        return true;
    }

    // The colour sits in this same dictionary and above this line, so the brush takes its own theme's value
    // rather than asking the theme system a second time.
    private static void AppendBrush(StringBuilder builder, string brushKey, string colorKey)
    {
        builder.Append($"{ThemeDictionaryIndent}<SolidColorBrush x:Key=\"{brushKey}\"\n");
        builder.Append($"{ThemeDictionaryIndent}                 Color=\"{{StaticResource {colorKey}}}\" />\n");
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
