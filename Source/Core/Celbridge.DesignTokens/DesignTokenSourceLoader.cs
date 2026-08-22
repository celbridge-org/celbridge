using System.Text.Json;

namespace Celbridge.DesignTokens;

/// <summary>
/// Reads the token source file, validating each entry as it goes so a malformed token fails the build
/// with a message naming it rather than producing broken markup.
/// </summary>
public static class DesignTokenSourceLoader
{
    private const string XamlTarget = "xaml";
    private const string CssTarget = "css";

    public static DesignTokenSource LoadFromFile(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = document.RootElement;

        var xamlHeader = ReadStringList(root, "xamlHeader");
        var cssHeader = ReadStringList(root, "cssHeader");
        var cssImports = ReadStringList(root, "cssImports");
        var groups = ReadGroups(root);

        var source = new DesignTokenSource
        {
            XamlHeader = xamlHeader,
            CssHeader = cssHeader,
            CssImports = cssImports,
            Groups = groups
        };

        ValidateXamlKeysAreUnique(source);

        return source;
    }

    // Every XAML key the generator emits shares one dictionary, so a key claimed twice would emit twice and
    // leave which declaration wins up to the parser.
    private static void ValidateXamlKeysAreUnique(DesignTokenSource source)
    {
        var claimedKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var token in source.Tokens)
        {
            var keys = new List<string?>
            {
                token.XamlColorKey,
                token.XamlBrushKey
            };

            keys.AddRange(token.XamlAliases);
            keys.AddRange(token.XamlColorAliases);

            foreach (var key in keys.OfType<string>())
            {
                if (claimedKeys.TryGetValue(key, out var owner))
                {
                    throw new InvalidDataException(
                        $"Token '{token.Key}' declares the XAML key '{key}', which token '{owner}' already declares.");
                }

                claimedKeys.Add(key, token.Key);
            }
        }
    }

    private static IReadOnlyList<DesignTokenGroup> ReadGroups(JsonElement root)
    {
        if (!root.TryGetProperty("groups", out var groupsElement))
        {
            throw new InvalidDataException("The token source has no 'groups' array.");
        }

        var groups = new List<DesignTokenGroup>();

        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            var comment = ReadStringList(groupElement, "comment");
            var tokens = ReadTokens(groupElement);

            groups.Add(new DesignTokenGroup
            {
                Comment = comment,
                Tokens = tokens
            });
        }

        return groups;
    }

    private static IReadOnlyList<DesignToken> ReadTokens(JsonElement groupElement)
    {
        if (!groupElement.TryGetProperty("tokens", out var tokensElement))
        {
            throw new InvalidDataException("A token group has no 'tokens' object.");
        }

        var tokens = new List<DesignToken>();

        foreach (var tokenProperty in tokensElement.EnumerateObject())
        {
            var token = ReadToken(tokenProperty.Name, tokenProperty.Value);
            tokens.Add(token);
        }

        return tokens;
    }

    private static DesignToken ReadToken(string key, JsonElement element)
    {
        var targets = ReadStringList(element, "targets");
        if (targets.Count == 0)
        {
            throw new InvalidDataException($"Token '{key}' declares no targets.");
        }

        foreach (var target in targets)
        {
            if (target != XamlTarget &&
                target != CssTarget)
            {
                throw new InvalidDataException($"Token '{key}' declares unknown target '{target}'.");
            }
        }

        var xamlColorKey = ReadOptionalString(element, XamlTarget);
        var cssPropertyName = ReadOptionalString(element, CssTarget);

        if (targets.Contains(XamlTarget) &&
            xamlColorKey is null)
        {
            throw new InvalidDataException($"Token '{key}' targets XAML but declares no 'xaml' name.");
        }

        if (targets.Contains(CssTarget) &&
            cssPropertyName is null)
        {
            throw new InvalidDataException($"Token '{key}' targets CSS but declares no 'css' name.");
        }

        var token = new DesignToken
        {
            Key = key,
            XamlColorKey = targets.Contains(XamlTarget) ? xamlColorKey : null,
            XamlBrushKey = ReadOptionalString(element, "xamlBrush"),
            XamlAliases = ReadStringList(element, "xamlAliases"),
            XamlColorAliases = ReadStringList(element, "xamlColorAliases"),
            CssPropertyName = targets.Contains(CssTarget) ? cssPropertyName : null,
            ThemeInvariantValue = ReadOptionalString(element, "value"),
            LightValue = ReadOptionalString(element, "light"),
            DarkValue = ReadOptionalString(element, "dark"),
            Published = ReadOptionalBoolean(element, "published"),
            Comment = ReadStringList(element, "comment"),
            DarkComment = ReadStringList(element, "darkComment")
        };

        ValidateValues(token);

        return token;
    }

    private static void ValidateValues(DesignToken token)
    {
        var hasThemePair = token.LightValue is not null && token.DarkValue is not null;

        if (token.IsThemeInvariant == hasThemePair)
        {
            throw new InvalidDataException(
                $"Token '{token.Key}' must declare either a 'value' or both 'light' and 'dark'.");
        }

        if (token.IsThemeInvariant &&
            (token.LightValue is not null || token.DarkValue is not null))
        {
            throw new InvalidDataException(
                $"Token '{token.Key}' declares a 'value', so its per-theme values would be ignored.");
        }

        if (!token.EmitsXaml)
        {
            // Each of these emits into the theme dictionaries over the token's XAML colour, so none of them
            // has anything to point at without that target.
            var brushDeclared = token.XamlBrushKey is not null;
            var aliasesDeclared = token.XamlAliases.Count > 0 || token.XamlColorAliases.Count > 0;

            if (brushDeclared || aliasesDeclared)
            {
                throw new InvalidDataException(
                    $"Token '{token.Key}' declares a XAML brush or alias but does not target XAML.");
            }

            return;
        }

        // A XAML Color parses hex only, so a token reaching that target cannot hold a CSS colour function.
        foreach (var theme in Enum.GetValues<DesignTokenTheme>())
        {
            var value = token.ValueForTheme(theme);
            if (!value.StartsWith('#'))
            {
                throw new InvalidDataException(
                    $"Token '{token.Key}' targets XAML, so its values must be hex, but the {theme} value is '{value}'.");
            }
        }
    }

    // Accepts either a single string or an array of them, so a one line comment needs no array wrapper.
    private static IReadOnlyList<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return [property.GetString()!];
        }

        return property.EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToList();
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.GetString();
    }

    private static bool ReadOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.GetBoolean();
    }
}
