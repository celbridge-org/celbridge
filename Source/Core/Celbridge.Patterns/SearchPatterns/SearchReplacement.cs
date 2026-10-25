using System.Text;

namespace Celbridge.Patterns;

/// <summary>
/// A replacement for the matches of a search pattern. For a regex, '$1' and '${1}' name a group by number, '$name'
/// and '${name}' name a group by name, and '$$' writes a '$'. Any other '$' is literal text, the same as in ripgrep's
/// --replace.
/// </summary>
public sealed class SearchReplacement
{
    // A part is literal text, or the number of a group. A group part writes the text the group matched.
    private readonly record struct Part(string? Text, int GroupNumber);

    private readonly IReadOnlyList<Part> _parts;

    private SearchReplacement(IReadOnlyList<Part> parts)
    {
        _parts = parts;
    }

    internal static Result<SearchReplacement> Compile(
        string replacement,
        bool isLiteral,
        int groupCount,
        IReadOnlyDictionary<string, int> groupNumbers)
    {
        if (isLiteral)
        {
            var literalParts = new List<Part>
            {
                new Part(replacement, 0),
            };
            return new SearchReplacement(literalParts);
        }

        var parts = new List<Part>();
        var text = new StringBuilder();
        var index = 0;
        while (index < replacement.Length)
        {
            var character = replacement[index];
            if (character != '$')
            {
                text.Append(character);
                index++;
                continue;
            }

            if (index + 1 < replacement.Length
                && replacement[index + 1] == '$')
            {
                text.Append('$');
                index += 2;
                continue;
            }

            if (!TryReadReference(replacement, index, out var name, out var referenceEnd))
            {
                text.Append('$');
                index++;
                continue;
            }

            var numberResult = ResolveGroup(name, groupCount, groupNumbers);
            if (numberResult.IsFailure)
            {
                return Result.Fail(numberResult.FirstErrorMessage);
            }
            var groupNumber = numberResult.Value;

            if (text.Length > 0)
            {
                parts.Add(new Part(text.ToString(), 0));
                text.Clear();
            }
            parts.Add(new Part(null, groupNumber));
            index = referenceEnd;
        }

        if (text.Length > 0)
        {
            parts.Add(new Part(text.ToString(), 0));
        }

        return new SearchReplacement(parts);
    }

    /// <summary>
    /// Returns the text that replaces a match.
    /// </summary>
    public string Expand(SearchMatch match)
    {
        var text = new StringBuilder();
        foreach (var part in _parts)
        {
            if (part.Text is not null)
            {
                text.Append(part.Text);
                continue;
            }

            var group = match.Groups[part.GroupNumber];
            if (group is not null)
            {
                text.Append(match.Text, group.Value.Start, group.Value.Length);
            }
        }

        return text.ToString();
    }

    // Reads the group name after a '$': letters, digits and '_', with or without braces around them.
    private static bool TryReadReference(string replacement, int dollarIndex, out string name, out int referenceEnd)
    {
        name = string.Empty;
        referenceEnd = dollarIndex;

        var nameStart = dollarIndex + 1;
        var isBraced = nameStart < replacement.Length
            && replacement[nameStart] == '{';
        if (isBraced)
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < replacement.Length
            && IsNameCharacter(replacement[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd == nameStart)
        {
            return false;
        }

        referenceEnd = nameEnd;
        if (isBraced)
        {
            if (nameEnd >= replacement.Length
                || replacement[nameEnd] != '}')
            {
                return false;
            }
            referenceEnd = nameEnd + 1;
        }

        name = replacement.Substring(nameStart, nameEnd - nameStart);

        return true;
    }

    private static bool IsNameCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character)
            || character == '_';
    }

    private static Result<int> ResolveGroup(string name, int groupCount, IReadOnlyDictionary<string, int> groupNumbers)
    {
        var isNumber = name.All(char.IsAsciiDigit);
        if (isNumber
            && int.TryParse(name, out var number)
            && number <= groupCount)
        {
            return number;
        }

        if (!isNumber
            && groupNumbers.TryGetValue(name, out var namedNumber))
        {
            return namedNumber;
        }

        var digitCount = name.TakeWhile(char.IsAsciiDigit).Count();
        if (digitCount > 0
            && digitCount < name.Length)
        {
            var digits = name.Substring(0, digitCount);
            var rest = name.Substring(digitCount);
            return Result.Fail($"In the replacement, '${name}' names a group called '{name}', and the pattern has none. Write '${{{digits}}}{rest}' to follow group {digits} with '{rest}'.");
        }

        return Result.Fail($"In the replacement, '${name}' names a group the pattern does not have.");
    }
}
