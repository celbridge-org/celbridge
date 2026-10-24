using System.Text;
using System.Text.RegularExpressions;

namespace Celbridge.Utilities;

/// <summary>
/// One line of a gitignore list, compiled with git's rules for reading the line and matching it.
/// </summary>
internal sealed class GitignoreRule
{
    private enum TokenKind
    {
        Literal,
        Separator,
        AnyCharacter,
        Stars,
        Class,
    }

    // Text is the character of a literal, or the regex of a class.
    private readonly record struct Token(TokenKind Kind, string Text, int StarCount);

    // Matches a line against the path, or against the path's last name.
    private interface ITextMatch
    {
        bool IsMatch(ReadOnlySpan<char> text);
    }

    private sealed class RegexMatch : ITextMatch
    {
        private readonly Regex _regex;

        public RegexMatch(Regex regex)
        {
            _regex = regex;
        }

        public bool IsMatch(ReadOnlySpan<char> text)
        {
            return _regex.IsMatch(text);
        }
    }

    // Git compares a name directly with a pattern that has no wildcard. For a pattern of one '*' and then literals,
    // git checks that the name ends with the literals.
    private sealed class LiteralMatch : ITextMatch
    {
        private readonly string _literal;
        private readonly bool _isSuffix;
        private readonly StringComparison _comparison;

        public LiteralMatch(string literal, bool isSuffix, StringComparison comparison)
        {
            _literal = literal;
            _isSuffix = isSuffix;
            _comparison = comparison;
        }

        public bool IsMatch(ReadOnlySpan<char> text)
        {
            if (_isSuffix)
            {
                return text.EndsWith(_literal, _comparison);
            }

            return text.Equals(_literal, _comparison);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> PosixClasses = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["alnum"] = "0-9A-Za-z",
        ["alpha"] = "A-Za-z",
        ["blank"] = @" \t",
        ["cntrl"] = @"\x00-\x1F\x7F",
        ["digit"] = "0-9",
        ["graph"] = @"\x21-\x7E",
        ["lower"] = "a-z",
        ["print"] = @"\x20-\x7E",
        ["punct"] = @"!-/:-@\[-`{-~",
        ["space"] = @" \t\n\r",
        ["upper"] = "A-Z",
        ["xdigit"] = "0-9A-Fa-f",
    };

    // The characters a '\' can escape. The comma separates the patterns of a search filter.
    private const string SpecialCharacters = @"\*?[]{}!#, ";

    // The characters a '\' can also escape inside a class.
    private const string ClassSpecialCharacters = "-^";

    private readonly ITextMatch _textMatch;

    // One regex for each name in a pattern with a slash. CanMatchInside builds the list on first use. A null entry can
    // match any number of names.
    private readonly Lazy<IReadOnlyList<Regex?>> _segmentRegexes;

    /// <summary>
    /// True when the line starts with '!', so a match puts the path back in.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// True when the line ends with '/', so the line matches folders only.
    /// </summary>
    public bool MatchesFoldersOnly { get; }

    /// <summary>
    /// True when the pattern has no slash, so the line matches the last name of a path at any depth.
    /// </summary>
    public bool MatchesName { get; }

    private GitignoreRule(
        bool isNegative,
        bool matchesFoldersOnly,
        bool matchesName,
        ITextMatch textMatch,
        Lazy<IReadOnlyList<Regex?>> segmentRegexes)
    {
        IsNegative = isNegative;
        MatchesFoldersOnly = matchesFoldersOnly;
        MatchesName = matchesName;
        _textMatch = textMatch;
        _segmentRegexes = segmentRegexes;
    }

    /// <summary>
    /// Returns the pattern of a line once git drops its trailing spaces, or an empty string when the line is blank
    /// or a comment.
    /// </summary>
    public static string ReadPattern(string line)
    {
        if (line.StartsWith('#'))
        {
            return string.Empty;
        }

        return TrimTrailingSpaces(line);
    }

    /// <summary>
    /// Compiles a pattern from ReadPattern. Fails with a message that says what to write instead when git would read
    /// the pattern differently from what its writer meant.
    /// </summary>
    public static Result<GitignoreRule> Compile(string pattern, bool ignoreCase)
    {
        // The engine that never backtracks matches in time linear in the length of the text.
        var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking;
        var comparison = StringComparison.Ordinal;
        if (ignoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
            comparison = StringComparison.OrdinalIgnoreCase;
        }

        var body = pattern;
        var isNegative = false;
        if (body.StartsWith('!'))
        {
            isNegative = true;
            body = body.Substring(1);
        }

        var tokenizeResult = Tokenize(pattern, body);
        if (tokenizeResult.IsFailure)
        {
            return Result.Fail(tokenizeResult.FirstErrorMessage);
        }
        var tokens = tokenizeResult.Value;

        // Git drops one trailing slash, and the line then matches folders only.
        var matchesFoldersOnly = false;
        if (tokens.Count > 0
            && tokens[^1].Kind == TokenKind.Separator)
        {
            matchesFoldersOnly = true;
            tokens.RemoveAt(tokens.Count - 1);
        }

        // A pattern with no slash left matches a name at any depth. Git counts a slash inside a class too.
        var matchesName = !SliceBeforeTrailingSlash(body).Contains('/');

        if (matchesName)
        {
            var nameMatch = BuildNameMatch(tokens, regexOptions, comparison);
            var noSegments = new Lazy<IReadOnlyList<Regex?>>(Array.Empty<Regex?>());
            return new GitignoreRule(isNegative, matchesFoldersOnly, matchesName: true, nameMatch, noSegments);
        }

        // Git drops one leading slash from a pattern that is relative to the base folder.
        if (tokens.Count > 0
            && tokens[0].Kind == TokenKind.Separator)
        {
            tokens.RemoveAt(0);
        }

        var pathMatch = new RegexMatch(new Regex(BuildPathRegex(tokens), regexOptions));
        var segmentRegexes = new Lazy<IReadOnlyList<Regex?>>(() => BuildSegmentRegexes(tokens, regexOptions));

        return new GitignoreRule(isNegative, matchesFoldersOnly, matchesName: false, pathMatch, segmentRegexes);
    }

    /// <summary>
    /// Returns true if the line matches the path. The name is the path's last name.
    /// </summary>
    public bool Matches(ReadOnlySpan<char> path, ReadOnlySpan<char> name, bool isFolder)
    {
        if (MatchesFoldersOnly
            && !isFolder)
        {
            return false;
        }

        if (MatchesName)
        {
            return _textMatch.IsMatch(name);
        }

        return _textMatch.IsMatch(path);
    }

    /// <summary>
    /// Returns false when the line cannot match any path inside the folder. A line that matches a name at any depth
    /// can match inside every folder.
    /// </summary>
    public bool CanMatchInside(string folderPath)
    {
        if (MatchesName)
        {
            return true;
        }

        var segmentRegexes = _segmentRegexes.Value;

        // Positions in the pattern where the path's next name may match.
        var positions = new HashSet<int>();
        AddPosition(positions, 0, segmentRegexes);

        if (!string.IsNullOrEmpty(folderPath))
        {
            foreach (var folderName in folderPath.Split('/'))
            {
                var nextPositions = new HashSet<int>();
                foreach (var position in positions)
                {
                    if (position == segmentRegexes.Count)
                    {
                        continue;
                    }

                    var segmentRegex = segmentRegexes[position];
                    if (segmentRegex is null)
                    {
                        AddPosition(nextPositions, position, segmentRegexes);
                    }
                    else if (segmentRegex.IsMatch(folderName))
                    {
                        AddPosition(nextPositions, position + 1, segmentRegexes);
                    }
                }

                if (nextPositions.Count == 0)
                {
                    return false;
                }

                positions = nextPositions;
            }
        }

        // A path inside the folder has at least one more name, so a part of the pattern must be left to match it.
        return positions.Any(position => position < segmentRegexes.Count);
    }

    // Adds the position. A part that can match any number of names can also match none, so the position after each
    // such part is added too.
    private static void AddPosition(HashSet<int> positions, int position, IReadOnlyList<Regex?> segmentRegexes)
    {
        while (positions.Add(position)
            && position < segmentRegexes.Count
            && segmentRegexes[position] is null)
        {
            position++;
        }
    }

    // Drops the spaces at the end of a line, except a space written with '\' before it. A line that ends in '\'
    // keeps its spaces.
    private static string TrimTrailingSpaces(string line)
    {
        int? trailingSpacesStart = null;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == ' ')
            {
                trailingSpacesStart ??= index;
                continue;
            }

            if (character == '\\')
            {
                index++;
                if (index == line.Length)
                {
                    return line;
                }
            }

            trailingSpacesStart = null;
        }

        if (trailingSpacesStart is null)
        {
            return line;
        }

        return line.Substring(0, trailingSpacesStart.Value);
    }

    private static string SliceBeforeTrailingSlash(string body)
    {
        if (body.EndsWith('/'))
        {
            return body.Substring(0, body.Length - 1);
        }

        return body;
    }

    private static Result<List<Token>> Tokenize(string pattern, string body)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < body.Length)
        {
            var character = body[index];
            switch (character)
            {
                case '\\':
                    {
                        var escapeResult = ReadEscape(pattern, body, index, isInClass: false);
                        if (escapeResult.IsFailure)
                        {
                            return Result.Fail(escapeResult.FirstErrorMessage);
                        }

                        tokens.Add(new Token(TokenKind.Literal, escapeResult.Value.ToString(), 0));
                        index += 2;
                        break;
                    }

                case '*':
                    {
                        var starCount = 0;
                        while (index < body.Length
                            && body[index] == '*')
                        {
                            starCount++;
                            index++;
                        }

                        tokens.Add(new Token(TokenKind.Stars, string.Empty, starCount));
                        break;
                    }

                case '?':
                    tokens.Add(new Token(TokenKind.AnyCharacter, string.Empty, 0));
                    index++;
                    break;

                case '[':
                    {
                        var classResult = ReadClass(pattern, body, index);
                        if (classResult.IsFailure)
                        {
                            return Result.Fail(classResult.FirstErrorMessage);
                        }

                        var characterClass = classResult.Value;
                        tokens.Add(new Token(TokenKind.Class, characterClass.Regex, 0));
                        index = characterClass.EndIndex;
                        break;
                    }

                case '{':
                case '}':
                    return Result.Fail(
                        $"'{pattern}' uses braces. Gitignore patterns have no braces, so write a separate pattern for " +
                        @"each alternative, or write '\{' and '\}' for literal braces.");

                case '/':
                    tokens.Add(new Token(TokenKind.Separator, string.Empty, 0));
                    index++;
                    break;

                default:
                    tokens.Add(new Token(TokenKind.Literal, character.ToString(), 0));
                    index++;
                    break;
            }
        }

        return tokens;
    }

    // Reads the character after the '\' at the index. A '\' escapes only a special character. Git drops a '\'
    // before any other character, which turns a path written with '\' between its names into one name.
    private static Result<char> ReadEscape(string pattern, string body, int index, bool isInClass)
    {
        if (index + 1 >= body.Length)
        {
            return Result.Fail($@"'{pattern}' ends in '\', which matches nothing. Remove it, or write '\\' for a backslash.");
        }

        var escaped = body[index + 1];
        var isSpecial = SpecialCharacters.Contains(escaped)
            || (isInClass && ClassSpecialCharacters.Contains(escaped));
        if (!isSpecial)
        {
            return Result.Fail(
                $@"In '{pattern}', '\{escaped}' is not an escape. Write '/' between folder names. " +
                @"A '\' escapes only a special character, like '\*' or '\['.");
        }

        return escaped;
    }

    private readonly record struct CharacterClass(string Regex, int EndIndex);

    // Reads the class that starts at the '[' at the index, the way git's wildmatch reads it. A ']' straight after
    // the '[' or the '!' is a literal. A class never matches '/'.
    private static Result<CharacterClass> ReadClass(string pattern, string body, int index)
    {
        var unclosedMessage = $@"In '{pattern}', a '[' has no closing ']'. Close it with ']', or write '\[' for a literal '['.";

        var builder = new StringBuilder();
        var position = index + 1;
        var isNegated = false;
        if (position < body.Length
            && (body[position] == '!' || body[position] == '^'))
        {
            isNegated = true;
            position++;
        }

        // The character before a '-', which starts a range. Null after a range or a named class.
        char? rangeStart = null;
        var isFirst = true;
        while (true)
        {
            if (position >= body.Length)
            {
                return Result.Fail(unclosedMessage);
            }

            var character = body[position];
            if (character == ']'
                && !isFirst)
            {
                position++;
                break;
            }

            isFirst = false;

            if (character == '\\')
            {
                if (position + 1 >= body.Length)
                {
                    return Result.Fail(unclosedMessage);
                }

                var escapeResult = ReadEscape(pattern, body, position, isInClass: true);
                if (escapeResult.IsFailure)
                {
                    return Result.Fail(escapeResult.FirstErrorMessage);
                }

                var escaped = escapeResult.Value;
                AppendClassCharacter(builder, escaped);
                rangeStart = escaped;
                position += 2;
                continue;
            }

            if (character == '-'
                && rangeStart is not null
                && position + 1 < body.Length
                && body[position + 1] != ']')
            {
                position++;
                var rangeEnd = body[position];
                if (rangeEnd == '\\')
                {
                    if (position + 1 >= body.Length)
                    {
                        return Result.Fail(unclosedMessage);
                    }

                    var escapeResult = ReadEscape(pattern, body, position, isInClass: true);
                    if (escapeResult.IsFailure)
                    {
                        return Result.Fail(escapeResult.FirstErrorMessage);
                    }

                    rangeEnd = escapeResult.Value;
                    position++;
                }

                // A range written in reverse matches nothing, and its start still matches as a literal.
                if (rangeStart.Value <= rangeEnd)
                {
                    AppendClassCharacter(builder, rangeStart.Value);
                    builder.Append('-');
                    AppendClassCharacter(builder, rangeEnd);
                }

                rangeStart = null;
                position++;
                continue;
            }

            if (character == '['
                && position + 1 < body.Length
                && body[position + 1] == ':')
            {
                var nameStart = position + 2;
                var closeIndex = body.IndexOf(']', nameStart);
                if (closeIndex < 0)
                {
                    return Result.Fail(unclosedMessage);
                }

                // Without ':]' the '[' is a literal, and reading goes on from the ':'.
                var nameLength = closeIndex - nameStart - 1;
                if (nameLength < 0
                    || body[closeIndex - 1] != ':')
                {
                    AppendClassCharacter(builder, '[');
                    rangeStart = '[';
                    position++;
                    continue;
                }

                var name = body.Substring(nameStart, nameLength);
                if (!PosixClasses.TryGetValue(name, out var ranges))
                {
                    return Result.Fail(
                        $@"In '{pattern}', '[:{name}:]' is not a character class. Use a class like '[:alpha:]' or " +
                        @"'[:digit:]', or write '\[' for a literal '['.");
                }

                builder.Append(ranges);
                rangeStart = null;
                position = closeIndex + 1;
                continue;
            }

            AppendClassCharacter(builder, character);
            rangeStart = character;
            position++;
        }

        string classRegex;
        if (isNegated)
        {
            classRegex = $"[^{builder}/]";
        }
        else
        {
            classRegex = $"[{builder}-[/]]";
        }

        return new CharacterClass(classRegex, position);
    }

    private static void AppendClassCharacter(StringBuilder builder, char character)
    {
        builder.Append(@"\u");
        builder.Append(((int)character).ToString("X4"));
    }

    // Returns a string compare for a name pattern of literals, or of one '*' and then literals. Any other pattern gets
    // a regex.
    private static ITextMatch BuildNameMatch(IReadOnlyList<Token> tokens, RegexOptions regexOptions, StringComparison comparison)
    {
        var isSuffix = tokens.Count > 0
            && tokens[0].Kind == TokenKind.Stars
            && tokens[0].StarCount == 1;
        var literalStart = isSuffix ? 1 : 0;

        var literal = new StringBuilder();
        for (var index = literalStart; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind != TokenKind.Literal)
            {
                return new RegexMatch(new Regex(BuildNameRegex(tokens), regexOptions));
            }

            literal.Append(token.Text);
        }

        return new LiteralMatch(literal.ToString(), isSuffix, comparison);
    }

    // A name has no slash, so every run of stars matches within the name.
    private static string BuildNameRegex(IReadOnlyList<Token> tokens)
    {
        var builder = new StringBuilder();
        builder.Append('^');
        foreach (var token in tokens)
        {
            AppendWithinName(builder, token);
        }
        builder.Append('$');

        return builder.ToString();
    }

    private static string BuildPathRegex(IReadOnlyList<Token> tokens)
    {
        var builder = new StringBuilder();
        builder.Append('^');

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == TokenKind.Stars
                && SpansFolders(tokens, index))
            {
                // A trailing '**' matches everything inside, and '**/' matches any number of folders.
                if (index == tokens.Count - 1)
                {
                    builder.Append(".*");
                    continue;
                }

                if (tokens[index + 1].Kind == TokenKind.Separator)
                {
                    builder.Append("(?:.*/)?");
                    index++;
                    continue;
                }
            }

            if (token.Kind == TokenKind.Separator)
            {
                builder.Append('/');
                continue;
            }

            AppendWithinName(builder, token);
        }

        builder.Append('$');

        return builder.ToString();
    }

    private static IReadOnlyList<Regex?> BuildSegmentRegexes(IReadOnlyList<Token> tokens, RegexOptions regexOptions)
    {
        var segmentRegexes = new List<Regex?>();

        var segmentTokens = new List<Token>();
        var segmentSpansFolders = false;
        for (var index = 0; index <= tokens.Count; index++)
        {
            if (index == tokens.Count
                || tokens[index].Kind == TokenKind.Separator)
            {
                if (segmentSpansFolders)
                {
                    segmentRegexes.Add(null);
                }
                else
                {
                    segmentRegexes.Add(new Regex(BuildNameRegex(segmentTokens), regexOptions));
                }

                segmentTokens.Clear();
                segmentSpansFolders = false;
                continue;
            }

            var token = tokens[index];
            if (token.Kind == TokenKind.Stars
                && SpansFolders(tokens, index))
            {
                segmentSpansFolders = true;
            }

            segmentTokens.Add(token);
        }

        return segmentRegexes;
    }

    private static void AppendWithinName(StringBuilder builder, Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Literal:
                builder.Append(Regex.Escape(token.Text));
                break;

            case TokenKind.Class:
                builder.Append(token.Text);
                break;

            case TokenKind.AnyCharacter:
                builder.Append("[^/]");
                break;

            case TokenKind.Stars:
                builder.Append("[^/]*");
                break;

            case TokenKind.Separator:
                builder.Append('/');
                break;
        }
    }

    // Returns true if the run of stars can match across folders. Such a run has two or more stars and starts a name.
    // The run also ends the pattern or comes before a slash.
    private static bool SpansFolders(IReadOnlyList<Token> tokens, int index)
    {
        var token = tokens[index];
        if (token.StarCount < 2)
        {
            return false;
        }

        var startsName = index == 0
            || tokens[index - 1].Kind == TokenKind.Separator;
        var endsName = index == tokens.Count - 1
            || tokens[index + 1].Kind == TokenKind.Separator;

        return startsName
            && endsName;
    }
}
