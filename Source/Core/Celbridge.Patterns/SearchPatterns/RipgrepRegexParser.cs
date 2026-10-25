using System.Globalization;
using System.Text;

namespace Celbridge.Patterns;

/// <summary>
/// A zero-width test in a parsed regex.
/// </summary>
internal enum RegexAssertion
{
    LineStart,
    LineEnd,
    WordBoundary,
    NotWordBoundary,
}

/// <summary>
/// A node of a parsed regex. Flags are already applied, so a node means the same thing wherever it appears.
/// </summary>
internal abstract record RegexNode;

/// <summary>
/// Matches one code point from the set.
/// </summary>
internal sealed record CharacterSetNode(CodePointSet Set) : RegexNode;

/// <summary>
/// Matches each item in turn. An empty sequence matches empty text.
/// </summary>
internal sealed record SequenceNode(IReadOnlyList<RegexNode> Items) : RegexNode;

/// <summary>
/// Matches the first branch that leads to a match.
/// </summary>
internal sealed record AlternationNode(IReadOnlyList<RegexNode> Branches) : RegexNode;

/// <summary>
/// Matches the item from Min to Max times. A null Max has no upper limit.
/// </summary>
internal sealed record RepetitionNode(RegexNode Item, int Min, int? Max, bool IsGreedy) : RegexNode;

/// <summary>
/// Matches the item and captures the text as the group with the number.
/// </summary>
internal sealed record CaptureNode(RegexNode Item, int Number) : RegexNode;

/// <summary>
/// Matches empty text at a position where the assertion is true.
/// </summary>
internal sealed record AssertionNode(RegexAssertion Assertion) : RegexNode;

/// <summary>
/// A parsed regex. GroupCount counts the capture groups, and GroupNumbers gives the number of each named group.
/// </summary>
internal sealed record ParsedRegex(RegexNode Root, int GroupCount, IReadOnlyDictionary<string, int> GroupNumbers);

/// <summary>
/// Parses a regex in ripgrep's dialect, the syntax of the Rust regex crate. The parser refuses what ripgrep refuses,
/// and the constructs that .NET cannot match the same way.
/// </summary>
internal sealed class RipgrepRegexParser
{
    private sealed class RefusalException : Exception
    {
        public RefusalException(string message)
            : base(message)
        {
        }
    }

    private record struct Flags(bool IgnoreCase, bool DotMatchesLineBreak, bool SwapGreed, bool IgnoreWhitespace);

    // A character class item. CodePoint is set when the item is one literal character, so it can bound a range.
    private readonly record struct ClassItem(CodePointSet Set, int? CodePoint);

    private enum ClassOperator
    {
        Intersection,
        Difference,
        SymmetricDifference,
    }

    private const int MaxHexValue = 0x10FFFF;

    private readonly string _pattern;
    private readonly Dictionary<string, int> _groupNumbers = new(StringComparer.Ordinal);
    private int _position;
    private int _groupCount;

    private RipgrepRegexParser(string pattern)
    {
        _pattern = pattern;
    }

    public static Result<ParsedRegex> Parse(string pattern, bool ignoreCase)
    {
        var parser = new RipgrepRegexParser(pattern);
        try
        {
            var flags = new Flags(ignoreCase, DotMatchesLineBreak: false, SwapGreed: false, IgnoreWhitespace: false);
            var root = parser.ParseAlternation(ref flags);
            if (!parser.IsEnd)
            {
                throw Refuse("A ')' has no opening '('. Write '\\)' for a literal parenthesis.");
            }

            return new ParsedRegex(root, parser._groupCount, parser._groupNumbers);
        }
        catch (RefusalException exception)
        {
            return Result.Fail(exception.Message);
        }
    }

    private bool IsEnd => _position >= _pattern.Length;

    private char Peek()
    {
        return PeekAt(_position);
    }

    private char PeekAt(int index)
    {
        if (index < _pattern.Length)
        {
            return _pattern[index];
        }

        return '\0';
    }

    private static RefusalException Refuse(string message)
    {
        return new RefusalException(message);
    }

    private RegexNode ParseAlternation(ref Flags flags)
    {
        var branches = new List<RegexNode>
        {
            ParseSequence(ref flags),
        };
        while (!IsEnd
            && Peek() == '|')
        {
            _position++;
            branches.Add(ParseSequence(ref flags));
        }

        if (branches.Count == 1)
        {
            return branches[0];
        }

        return new AlternationNode(branches);
    }

    private RegexNode ParseSequence(ref Flags flags)
    {
        var items = new List<RegexNode>();
        var canRepeat = false;
        while (true)
        {
            SkipIgnoredWhitespace(flags);
            if (IsEnd)
            {
                break;
            }

            var character = Peek();
            if (character == '|'
                || character == ')')
            {
                break;
            }

            if (character == '*'
                || character == '+'
                || character == '?'
                || character == '{')
            {
                if (!canRepeat)
                {
                    throw Refuse($"The '{character}' at position {_position + 1} repeats nothing. Write '\\{character}' for a literal '{character}'.");
                }

                items[^1] = ParseRepetition(items[^1], flags);
                continue;
            }

            var atom = ParseAtom(ref flags);
            if (atom is null)
            {
                canRepeat = false;
                continue;
            }

            items.Add(atom);
            canRepeat = true;
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        return new SequenceNode(items);
    }

    private RegexNode ParseRepetition(RegexNode item, Flags flags)
    {
        var character = Peek();
        int min;
        int? max;
        if (character == '{')
        {
            (min, max) = ParseCountedRepetition(flags);
        }
        else
        {
            _position++;
            min = character == '+' ? 1 : 0;
            max = character == '?' ? 1 : null;
        }

        SkipIgnoredWhitespace(flags);
        var isLazy = false;
        if (!IsEnd
            && Peek() == '?')
        {
            isLazy = true;
            _position++;
        }

        var isGreedy = isLazy == flags.SwapGreed;

        return new RepetitionNode(item, min, max, isGreedy);
    }

    private (int Min, int? Max) ParseCountedRepetition(Flags flags)
    {
        var braceStart = _position;
        _position++;
        SkipIgnoredWhitespace(flags);

        var min = ParseDecimal();
        if (min is null)
        {
            throw Refuse(NoRepetitionMessage(braceStart));
        }

        SkipIgnoredWhitespace(flags);
        int? max = min;
        if (Peek() == ',')
        {
            _position++;
            SkipIgnoredWhitespace(flags);
            if (Peek() == '}')
            {
                max = null;
            }
            else
            {
                max = ParseDecimal();
                if (max is null)
                {
                    throw Refuse(NoRepetitionMessage(braceStart));
                }
            }
            SkipIgnoredWhitespace(flags);
        }

        if (IsEnd
            || Peek() != '}')
        {
            throw Refuse(NoRepetitionMessage(braceStart));
        }
        _position++;

        if (max < min)
        {
            throw Refuse($"In '{_pattern.Substring(braceStart, _position - braceStart)}', the first count is larger than the second.");
        }

        return (min.Value, max);
    }

    private static string NoRepetitionMessage(int braceStart)
    {
        return $"The '{{' at position {braceStart + 1} starts no repetition. A repetition looks like '{{2}}', '{{2,}}' or '{{2,5}}'. Write '\\{{' for a literal brace.";
    }

    // Reads a repetition count. Whitespace around the digits is skipped with or without the 'x' flag, the same as in
    // ripgrep.
    private int? ParseDecimal()
    {
        SkipWhitespace();

        var start = _position;
        long value = 0;
        while (!IsEnd
            && char.IsAsciiDigit(Peek()))
        {
            value = value * 10 + (Peek() - '0');
            if (value > int.MaxValue)
            {
                throw Refuse("A repetition count is too large.");
            }
            _position++;
        }

        if (_position == start)
        {
            return null;
        }

        SkipWhitespace();

        return (int)value;
    }

    private void SkipWhitespace()
    {
        while (!IsEnd
            && char.IsWhiteSpace(Peek()))
        {
            _position++;
        }
    }

    // Returns null for a group that only sets flags, like '(?i)'.
    private RegexNode? ParseAtom(ref Flags flags)
    {
        var character = Peek();
        switch (character)
        {
            case '(':
                return ParseGroup(ref flags);

            case '[':
                _position++;
                var classSet = ParseClass(flags);
                return new CharacterSetNode(classSet);

            case '.':
                _position++;
                return new CharacterSetNode(DotSet(flags));

            case '^':
                _position++;
                return new AssertionNode(RegexAssertion.LineStart);

            case '$':
                _position++;
                return new AssertionNode(RegexAssertion.LineEnd);

            case '\\':
                return ParseEscape(flags);

            default:
                var codePoint = ReadCodePoint();
                var literal = LiteralItem(codePoint, flags);
                return new CharacterSetNode(literal.Set);
        }
    }

    private static CodePointSet DotSet(Flags flags)
    {
        if (flags.DotMatchesLineBreak)
        {
            return CodePointSet.All;
        }

        var lineBreaks = CodePointSet.FromRangePairs(
        [
            '\n', '\n',
            '\r', '\r',
        ]);

        return CodePointSet.All.Except(lineBreaks);
    }

    private RegexNode? ParseGroup(ref Flags outerFlags)
    {
        var groupStart = _position;
        _position++;
        SkipIgnoredWhitespace(outerFlags);

        var innerFlags = outerFlags;
        int? captureNumber = null;
        if (Peek() == '?')
        {
            _position++;
            var next = Peek();
            var afterNext = PeekAt(_position + 1);
            if (next == ':')
            {
                _position++;
            }
            else if (next == '='
                || next == '!'
                || (next == '<' && (afterNext == '=' || afterNext == '!')))
            {
                throw Refuse(LookaroundMessage());
            }
            else if (next == 'P'
                && afterNext == '=')
            {
                throw Refuse(BackreferenceMessage());
            }
            else if (next == 'P'
                && afterNext == '<')
            {
                _position += 2;
                captureNumber = ParseGroupName();
            }
            else if (next == '<')
            {
                _position++;
                captureNumber = ParseGroupName();
            }
            else
            {
                var flagsEnd = ParseFlags(ref innerFlags);
                if (flagsEnd == ')')
                {
                    outerFlags = innerFlags;
                    return null;
                }
            }
        }
        else
        {
            _groupCount++;
            captureNumber = _groupCount;
        }

        var inner = ParseAlternation(ref innerFlags);
        if (IsEnd
            || Peek() != ')')
        {
            throw Refuse($"The '(' at position {groupStart + 1} has no closing ')'. Write '\\(' for a literal parenthesis.");
        }
        _position++;

        if (captureNumber is null)
        {
            return inner;
        }

        return new CaptureNode(inner, captureNumber.Value);
    }

    private static string LookaroundMessage()
    {
        return "Lookaround, like '(?=' or '(?<!', is not supported, the same as in ripgrep. Match the surrounding text as part of the pattern.";
    }

    private static string BackreferenceMessage()
    {
        return "Backreferences, like '\\1', are not supported, the same as in ripgrep.";
    }

    private int ParseGroupName()
    {
        var nameStart = _position;
        var nameEnd = _pattern.IndexOf('>', nameStart);
        if (nameEnd < 0)
        {
            throw Refuse($"The group name at position {nameStart + 1} has no closing '>'.");
        }

        var name = _pattern.Substring(nameStart, nameEnd - nameStart);
        if (!IsValidGroupName(name))
        {
            throw Refuse($"'{name}' is not a valid group name. A name starts with a letter or '_', and continues with letters, digits, '_', '.', '[' or ']'.");
        }

        if (_groupNumbers.ContainsKey(name))
        {
            throw Refuse($"The pattern names two groups '{name}'.");
        }

        _position = nameEnd + 1;
        _groupCount++;
        _groupNumbers[name] = _groupCount;

        return _groupCount;
    }

    private static bool IsValidGroupName(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        var isFirst = true;
        foreach (var rune in name.EnumerateRunes())
        {
            var isLetter = Rune.IsLetter(rune)
                || Rune.GetUnicodeCategory(rune) == UnicodeCategory.LetterNumber;
            var isValid = rune.Value == '_' || isLetter;
            if (!isFirst)
            {
                isValid = isValid
                    || rune.Value == '.'
                    || rune.Value == '['
                    || rune.Value == ']'
                    || Rune.IsNumber(rune);
            }

            if (!isValid)
            {
                return false;
            }
            isFirst = false;
        }

        return true;
    }

    // Returns ':' or ')', whichever ends the flags.
    private char ParseFlags(ref Flags flags)
    {
        var isNegated = false;
        var endsInNegation = false;
        var hasFlag = false;
        var seenFlags = new HashSet<char>();
        while (true)
        {
            if (IsEnd)
            {
                throw Refuse("A '(?' at the end of the pattern has no closing ')'.");
            }

            var character = Peek();
            if (character == ':'
                || character == ')')
            {
                if (endsInNegation)
                {
                    throw Refuse("A '-' in a flag group needs a flag after it, like '(?-i)'.");
                }

                if (!hasFlag)
                {
                    throw Refuse("'(?)' sets no flags. Write '\\(\\?\\)' for the literal text.");
                }

                _position++;
                return character;
            }

            if (character == '-')
            {
                if (isNegated)
                {
                    throw Refuse("A flag group has more than one '-'.");
                }

                isNegated = true;
                endsInNegation = true;
                _position++;
                continue;
            }

            if (!seenFlags.Add(character))
            {
                throw Refuse($"A flag group sets '{character}' twice.");
            }

            switch (character)
            {
                case 'i':
                    flags.IgnoreCase = !isNegated;
                    break;

                case 's':
                    flags.DotMatchesLineBreak = !isNegated;
                    break;

                case 'U':
                    flags.SwapGreed = !isNegated;
                    break;

                case 'x':
                    flags.IgnoreWhitespace = !isNegated;
                    break;

                case 'm':
                    if (isNegated)
                    {
                        throw Refuse("'(?-m)' is not supported. '^' and '$' always match at the start and end of each line.");
                    }
                    break;

                case 'u':
                    if (isNegated)
                    {
                        throw Refuse("'(?-u)' is not supported. Write an ASCII class, like '[0-9A-Za-z_]' for an ASCII '\\w'.");
                    }
                    break;

                case 'R':
                    if (isNegated)
                    {
                        throw Refuse("'(?-R)' is not supported. A line always ends at '\\n' or '\\r\\n'.");
                    }
                    break;

                default:
                    throw Refuse($"'{character}' is not a flag. The flags are i, m, s, U, u, x and R.");
            }

            hasFlag = true;
            endsInNegation = false;
            _position++;
        }
    }

    private RegexNode ParseEscape(Flags flags)
    {
        var escapeStart = _position;
        _position++;
        if (IsEnd)
        {
            throw Refuse("The pattern ends in '\\'. Write '\\\\' for a literal backslash.");
        }

        var character = Peek();
        switch (character)
        {
            case 'b':
                _position++;
                if (Peek() == '{')
                {
                    throw Refuse("Word boundaries like '\\b{start}' are not supported. Write '\\b' for a word boundary.");
                }
                return new AssertionNode(RegexAssertion.WordBoundary);

            case 'B':
                _position++;
                return new AssertionNode(RegexAssertion.NotWordBoundary);

            case 'A':
                throw Refuse("'\\A' is not supported. Write '^' for the start of a line.");

            case 'z':
                throw Refuse("'\\z' is not supported. Write '$' for the end of a line.");

            case '<':
            case '>':
                throw Refuse($"'\\{character}' is not supported. Write '\\b' for a word boundary.");
        }

        if (char.IsAsciiDigit(character))
        {
            throw Refuse(BackreferenceMessage());
        }

        var item = ParseEscapeBody(flags, escapeStart);

        return new CharacterSetNode(item.Set);
    }

    // Parses the escape after its '\', for the escapes that stand for a character or a class.
    private ClassItem ParseEscapeBody(Flags flags, int escapeStart)
    {
        var character = Peek();
        _position++;
        switch (character)
        {
            case 'a':
                return LiteralItem(0x07, flags);

            case 'f':
                return LiteralItem(0x0C, flags);

            case 't':
                return LiteralItem('\t', flags);

            case 'n':
                return LiteralItem('\n', flags);

            case 'r':
                return LiteralItem('\r', flags);

            case 'v':
                return LiteralItem(0x0B, flags);

            case 'x':
            case 'u':
            case 'U':
                var codePoint = ParseHex(character, flags);
                return LiteralItem(codePoint, flags);

            case 'd':
                return ClassItemOf(UnicodeClasses.Digit, isNegated: false, flags);

            case 'D':
                return ClassItemOf(UnicodeClasses.Digit, isNegated: true, flags);

            case 's':
                return ClassItemOf(UnicodeClasses.Space, isNegated: false, flags);

            case 'S':
                return ClassItemOf(UnicodeClasses.Space, isNegated: true, flags);

            case 'w':
                return ClassItemOf(UnicodeClasses.Word, isNegated: false, flags);

            case 'W':
                return ClassItemOf(UnicodeClasses.Word, isNegated: true, flags);

            case 'p':
            case 'P':
                return ParseProperty(isNegated: character == 'P', flags);
        }

        if (char.IsAscii(character)
            && !char.IsAsciiLetterOrDigit(character)
            && character != '<'
            && character != '>')
        {
            return LiteralItem(character, flags);
        }

        if (char.IsHighSurrogate(character)
            && _position < _pattern.Length)
        {
            _position++;
        }

        var escape = _pattern.Substring(escapeStart, _position - escapeStart);
        throw Refuse($"'{escape}' is not an escape in ripgrep's regex. Write '\\\\' for a literal backslash.");
    }

    private int ParseHex(char kind, Flags flags)
    {
        long value = 0;
        if (Peek() == '{')
        {
            _position++;
            var digitCount = 0;
            while (true)
            {
                SkipIgnoredWhitespace(flags);
                if (IsEnd)
                {
                    throw Refuse($"A '\\{kind}{{' has no closing '}}'.");
                }

                var character = Peek();
                if (character == '}')
                {
                    _position++;
                    break;
                }

                if (!char.IsAsciiHexDigit(character))
                {
                    throw Refuse($"'{character}' is not a hexadecimal digit in '\\{kind}{{...}}'.");
                }

                value = value * 16 + Convert.ToInt32(character.ToString(), 16);
                if (value > MaxHexValue)
                {
                    throw Refuse($"A '\\{kind}{{...}}' escape is above U+10FFFF, the last Unicode code point.");
                }
                digitCount++;
                _position++;
            }

            if (digitCount == 0)
            {
                throw Refuse($"'\\{kind}{{}}' has no digits.");
            }
        }
        else
        {
            var length = kind switch
            {
                'x' => 2,
                'u' => 4,
                _ => 8,
            };
            for (var index = 0; index < length; index++)
            {
                if (IsEnd
                    || !char.IsAsciiHexDigit(Peek()))
                {
                    throw Refuse($"'\\{kind}' takes exactly {length} hexadecimal digits, or any number in braces, like '\\{kind}{{41}}'.");
                }

                value = value * 16 + Convert.ToInt32(Peek().ToString(), 16);
                _position++;
            }

            if (value > MaxHexValue)
            {
                throw Refuse($"A '\\{kind}' escape is above U+10FFFF, the last Unicode code point.");
            }
        }

        if (value >= 0xD800
            && value <= 0xDFFF)
        {
            throw Refuse($"A '\\{kind}' escape names a surrogate, U+{value:X4}, which is not a character.");
        }

        return (int)value;
    }

    private ClassItem ParseProperty(bool isNegated, Flags flags)
    {
        string name;
        if (Peek() == '{')
        {
            var nameEnd = _pattern.IndexOf('}', _position);
            if (nameEnd < 0)
            {
                throw Refuse("A '\\p{' has no closing '}'.");
            }

            name = _pattern.Substring(_position + 1, nameEnd - _position - 1);
            _position = nameEnd + 1;
        }
        else
        {
            if (IsEnd)
            {
                throw Refuse("The pattern ends in '\\p'. Name a property, like '\\pL' or '\\p{Lu}'.");
            }

            var codePoint = ReadCodePoint();
            name = char.ConvertFromUtf32(codePoint);
        }

        if (!UnicodeClasses.TryGetProperty(name, out var set))
        {
            throw Refuse($"'\\p{{{name}}}' is a Unicode property that search patterns do not support. Use a general category, like '\\p{{L}}' for letters, '\\p{{Lu}}' for uppercase letters or '\\p{{Nd}}' for digits.");
        }

        return ClassItemOf(set, isNegated, flags);
    }

    // Parses a bracketed class after its '['. A ']' straight after the '[' or '[^' is literal, and so is any '-'
    // there. Inside, '&&', '--' and '~~' give the intersection, difference and symmetric difference.
    private CodePointSet ParseClass(Flags flags)
    {
        var classStart = _position - 1;
        SkipIgnoredWhitespace(flags);

        var isNegated = false;
        if (Peek() == '^')
        {
            isNegated = true;
            _position++;
            SkipIgnoredWhitespace(flags);
        }

        var union = new CodePointSet.Builder();
        var isUnionEmpty = true;
        while (!IsEnd
            && Peek() == '-')
        {
            union.Add(LiteralItem('-', flags).Set);
            isUnionEmpty = false;
            _position++;
            SkipIgnoredWhitespace(flags);
        }

        if (isUnionEmpty
            && Peek() == ']')
        {
            union.Add(LiteralItem(']', flags).Set);
            _position++;
        }

        CodePointSet? left = null;
        ClassOperator? pendingOperator = null;
        while (true)
        {
            SkipIgnoredWhitespace(flags);
            if (IsEnd)
            {
                throw Refuse($"The '[' at position {classStart + 1} has no closing ']'. Write '\\[' for a literal bracket.");
            }

            var character = Peek();
            var next = PeekAt(_position + 1);
            if (character == ']')
            {
                _position++;
                break;
            }

            if (character == '[')
            {
                if (TryParsePosixClass(flags, out var posixSet))
                {
                    union.Add(posixSet);
                    continue;
                }

                _position++;
                var nestedSet = ParseClass(flags);
                union.Add(nestedSet);
                continue;
            }

            ClassOperator? classOperator = null;
            if (character == '&'
                && next == '&')
            {
                classOperator = ClassOperator.Intersection;
            }
            else if (character == '-'
                && next == '-')
            {
                classOperator = ClassOperator.Difference;
            }
            else if (character == '~'
                && next == '~')
            {
                classOperator = ClassOperator.SymmetricDifference;
            }

            if (classOperator is not null)
            {
                _position += 2;
                var operand = union.ToSet();
                if (left is null
                    || pendingOperator is null)
                {
                    left = operand;
                }
                else
                {
                    left = ApplyClassOperator(left, pendingOperator.Value, operand);
                }
                pendingOperator = classOperator;
                union = new CodePointSet.Builder();
                continue;
            }

            var rangeSet = ParseClassRange(flags);
            union.Add(rangeSet);
        }

        var result = union.ToSet();
        if (left is not null
            && pendingOperator is not null)
        {
            result = ApplyClassOperator(left, pendingOperator.Value, result);
        }

        if (isNegated)
        {
            result = result.Complement();
        }

        return result;
    }

    private static CodePointSet ApplyClassOperator(CodePointSet left, ClassOperator classOperator, CodePointSet right)
    {
        return classOperator switch
        {
            ClassOperator.Intersection => left.Intersect(right),
            ClassOperator.Difference => left.Except(right),
            _ => left.SymmetricExcept(right),
        };
    }

    private CodePointSet ParseClassRange(Flags flags)
    {
        var start = ParseClassItem(flags);
        SkipIgnoredWhitespace(flags);
        if (IsEnd
            || Peek() != '-')
        {
            return start.Set;
        }

        // A '-' before the closing ']' or before another '-' is a literal, read on the next pass.
        var dashPosition = _position;
        _position++;
        SkipIgnoredWhitespace(flags);
        if (IsEnd
            || Peek() == ']'
            || Peek() == '-')
        {
            _position = dashPosition;
            return start.Set;
        }

        var end = ParseClassItem(flags);
        if (start.CodePoint is null
            || end.CodePoint is null)
        {
            throw Refuse("A range in a class needs a character at each end, like '[a-z]'. Write '\\-' for a literal '-'.");
        }

        if (start.CodePoint > end.CodePoint)
        {
            throw Refuse($"The range '{char.ConvertFromUtf32(start.CodePoint.Value)}-{char.ConvertFromUtf32(end.CodePoint.Value)}' starts after it ends.");
        }

        var range = CodePointSet.FromRange(start.CodePoint.Value, end.CodePoint.Value);
        if (flags.IgnoreCase)
        {
            range = UnicodeClasses.CaseFold(range);
        }

        return range;
    }

    private ClassItem ParseClassItem(Flags flags)
    {
        if (IsEnd)
        {
            throw Refuse("A '[' has no closing ']'. Write '\\[' for a literal bracket.");
        }

        if (Peek() != '\\')
        {
            var codePoint = ReadCodePoint();
            return LiteralItem(codePoint, flags);
        }

        var escapeStart = _position;
        _position++;
        if (IsEnd)
        {
            throw Refuse("The pattern ends in '\\'. Write '\\\\' for a literal backslash.");
        }

        return ParseEscapeBody(flags, escapeStart);
    }

    // Reads an ASCII class like '[:alpha:]' or '[:^alpha:]'. Leaves the position alone when the text is not one, so
    // the '[' opens a nested class.
    private bool TryParsePosixClass(Flags flags, out CodePointSet set)
    {
        set = CodePointSet.Empty;
        if (PeekAt(_position + 1) != ':')
        {
            return false;
        }

        var index = _position + 2;
        var isNegated = false;
        if (PeekAt(index) == '^')
        {
            isNegated = true;
            index++;
        }

        var nameEnd = _pattern.IndexOf(':', index);
        if (nameEnd < 0
            || PeekAt(nameEnd + 1) != ']')
        {
            return false;
        }

        var name = _pattern.Substring(index, nameEnd - index);
        if (!UnicodeClasses.TryGetPosixClass(name, out var posixSet))
        {
            return false;
        }

        _position = nameEnd + 2;
        set = ClassItemOf(posixSet, isNegated, flags).Set;

        return true;
    }

    private static ClassItem LiteralItem(int codePoint, Flags flags)
    {
        var set = CodePointSet.FromCodePoint(codePoint);
        if (flags.IgnoreCase)
        {
            set = UnicodeClasses.CaseFold(set);
        }

        return new ClassItem(set, codePoint);
    }

    // Folds letter case before negating, the same as ripgrep, so '(?i)[^k]' matches neither 'k' nor 'K'.
    private static ClassItem ClassItemOf(CodePointSet set, bool isNegated, Flags flags)
    {
        if (flags.IgnoreCase)
        {
            set = UnicodeClasses.CaseFold(set);
        }

        if (isNegated)
        {
            set = set.Complement();
        }

        return new ClassItem(set, null);
    }

    private int ReadCodePoint()
    {
        var character = _pattern[_position];
        if (char.IsHighSurrogate(character)
            && _position + 1 < _pattern.Length
            && char.IsLowSurrogate(_pattern[_position + 1]))
        {
            var codePoint = char.ConvertToUtf32(character, _pattern[_position + 1]);
            _position += 2;
            return codePoint;
        }

        if (char.IsSurrogate(character))
        {
            throw Refuse("The pattern contains half of a surrogate pair, which is not a character.");
        }

        _position++;
        return character;
    }

    // With the 'x' flag, whitespace is ignored. A '#' would start a comment, and ripgrep refuses every comment, since
    // the comment runs on past the end of the pattern.
    private void SkipIgnoredWhitespace(Flags flags)
    {
        if (!flags.IgnoreWhitespace)
        {
            return;
        }

        SkipWhitespace();
        if (!IsEnd
            && Peek() == '#')
        {
            throw Refuse("Comments with the 'x' flag are not supported, the same as in ripgrep. Write '\\#' for a literal '#'.");
        }
    }
}
