using System.Text;

namespace Celbridge.Utilities;

/// <summary>
/// Expands the braces in a glob, so 'src/*.{ts,tsx}' becomes 'src/*.ts' and 'src/*.tsx'. Braces can nest. A
/// character after '\' and the characters inside a class like '[{,]' stay as written.
/// </summary>
public static class BraceExpander
{
    /// <summary>
    /// Caps how many patterns one glob can expand to.
    /// </summary>
    public const int MaxPatterns = 100;

    /// <summary>
    /// Returns the glob's expansion, in order. Fails on an unbalanced brace, or when the glob would expand to more
    /// than MaxPatterns patterns.
    /// </summary>
    public static Result<IReadOnlyList<string>> Expand(string glob)
    {
        var parser = new Parser(glob);
        var patterns = parser.ParseSequence(insideBraces: false);
        if (parser.Error is not null)
        {
            return Result.Fail(parser.Error);
        }

        return Result<IReadOnlyList<string>>.Ok(patterns);
    }

    // Returns the index after the class that starts at the '[', the way a gitignore line reads a class. A class
    // that is not closed runs to the end of the glob, and the gitignore matcher refuses it.
    internal static int FindClassEnd(string glob, int classStart)
    {
        var position = classStart + 1;
        if (position < glob.Length
            && (glob[position] == '!' || glob[position] == '^'))
        {
            position++;
        }

        var isFirst = true;
        while (position < glob.Length)
        {
            var character = glob[position];
            if (character == ']'
                && !isFirst)
            {
                return position + 1;
            }

            isFirst = false;

            if (character == '\\')
            {
                position += 2;
                continue;
            }

            // A named class like '[:alpha:]' ends at the first ']', when a ':' comes before it.
            if (character == '['
                && position + 1 < glob.Length
                && glob[position + 1] == ':')
            {
                var closeIndex = glob.IndexOf(']', position + 2);
                if (closeIndex > position + 2
                    && glob[closeIndex - 1] == ':')
                {
                    position = closeIndex + 1;
                    continue;
                }
            }

            position++;
        }

        return glob.Length;
    }

    private sealed class Parser
    {
        private readonly string _glob;
        private int _index;

        public string? Error { get; private set; }

        public Parser(string glob)
        {
            _glob = glob;
        }

        // Reads up to the end of the glob, or inside braces up to the ',' or '}' that ends an alternative.
        public List<string> ParseSequence(bool insideBraces)
        {
            var patterns = new List<string>
            {
                string.Empty
            };
            var literal = new StringBuilder();

            while (_index < _glob.Length
                && Error is null)
            {
                var character = _glob[_index];
                if (character == '\\')
                {
                    var escapeLength = Math.Min(2, _glob.Length - _index);
                    literal.Append(_glob, _index, escapeLength);
                    _index += escapeLength;
                    continue;
                }

                if (character == '[')
                {
                    var classEnd = FindClassEnd(_glob, _index);
                    literal.Append(_glob, _index, classEnd - _index);
                    _index = classEnd;
                    continue;
                }

                if (insideBraces
                    && (character == ',' || character == '}'))
                {
                    break;
                }

                if (character == '}')
                {
                    Error = $@"In '{_glob}', a '}}' has no opening '{{'. Write '\}}' for a literal brace.";
                    break;
                }

                if (character == '{')
                {
                    patterns = Append(patterns, literal.ToString());
                    literal.Clear();

                    var alternatives = ParseAlternatives();
                    if (Error is not null)
                    {
                        break;
                    }

                    patterns = Combine(patterns, alternatives);
                    continue;
                }

                literal.Append(character);
                _index++;
            }

            return Append(patterns, literal.ToString());
        }

        // Reads the alternatives of the group that starts at the '{', and the '}' that closes the group.
        private List<string> ParseAlternatives()
        {
            _index++;

            var alternatives = new List<string>();
            while (Error is null)
            {
                var alternative = ParseSequence(insideBraces: true);
                if (Error is not null)
                {
                    break;
                }

                alternatives.AddRange(alternative);
                if (alternatives.Count > MaxPatterns)
                {
                    Error = TooManyPatternsMessage();
                    break;
                }

                if (_index >= _glob.Length)
                {
                    Error = $@"In '{_glob}', a '{{' has no closing '}}'. Close it with '}}', or write '\{{' for a literal brace.";
                    break;
                }

                var separator = _glob[_index];
                _index++;
                if (separator == '}')
                {
                    break;
                }
            }

            return alternatives;
        }

        private List<string> Combine(List<string> patterns, List<string> alternatives)
        {
            if (patterns.Count * alternatives.Count > MaxPatterns)
            {
                Error = TooManyPatternsMessage();
                return patterns;
            }

            var combined = new List<string>();
            foreach (var pattern in patterns)
            {
                foreach (var alternative in alternatives)
                {
                    combined.Add(pattern + alternative);
                }
            }

            return combined;
        }

        private static List<string> Append(List<string> patterns, string text)
        {
            if (text.Length == 0)
            {
                return patterns;
            }

            return patterns
                .Select(pattern => pattern + text)
                .ToList();
        }

        private string TooManyPatternsMessage()
        {
            return $"'{_glob}' expands to more than {MaxPatterns} patterns. Write fewer alternatives in its braces.";
        }
    }
}
