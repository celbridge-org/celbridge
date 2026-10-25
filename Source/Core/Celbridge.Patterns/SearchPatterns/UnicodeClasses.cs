using System.Globalization;

namespace Celbridge.Patterns;

/// <summary>
/// The character classes of ripgrep's regex dialect, as code point sets.
/// </summary>
internal static class UnicodeClasses
{
    private static readonly Lazy<CodePointSet[]> _categories = new(BuildCategories);

    private static readonly Lazy<CodePointSet> _word = new(() => CodePointSet.FromRangePairs(RipgrepUnicodeTables.WordRanges));

    private static readonly Lazy<CaseFoldTable> _caseFoldTable = new(BuildCaseFoldTable);

    private static readonly IReadOnlyDictionary<string, UnicodeCategory[]> GeneralCategories = BuildGeneralCategoryNames();

    private static readonly IReadOnlyDictionary<string, CodePointSet> PosixClasses = new Dictionary<string, CodePointSet>(StringComparer.Ordinal)
    {
        ["alnum"] = Ranges('0', '9', 'A', 'Z', 'a', 'z'),
        ["alpha"] = Ranges('A', 'Z', 'a', 'z'),
        ["ascii"] = Ranges(0x00, 0x7F),
        ["blank"] = Ranges('\t', '\t', ' ', ' '),
        ["cntrl"] = Ranges(0x00, 0x1F, 0x7F, 0x7F),
        ["digit"] = Ranges('0', '9'),
        ["graph"] = Ranges('!', '~'),
        ["lower"] = Ranges('a', 'z'),
        ["print"] = Ranges(' ', '~'),
        ["punct"] = Ranges('!', '/', ':', '@', '[', '`', '{', '~'),
        ["space"] = Ranges('\t', '\r', ' ', ' '),
        ["upper"] = Ranges('A', 'Z'),
        ["word"] = Ranges('0', '9', 'A', 'Z', '_', '_', 'a', 'z'),
        ["xdigit"] = Ranges('0', '9', 'A', 'F', 'a', 'f'),
    };

    /// <summary>
    /// Returns the code points of \d, Unicode's decimal digits.
    /// </summary>
    public static CodePointSet Digit => Category(UnicodeCategory.DecimalDigitNumber);

    /// <summary>
    /// Returns the code points of \s, Unicode's White_Space property.
    /// </summary>
    public static CodePointSet Space { get; } = Ranges(
        0x09, 0x0D,
        0x20, 0x20,
        0x85, 0x85,
        0xA0, 0xA0,
        0x1680, 0x1680,
        0x2000, 0x200A,
        0x2028, 0x2029,
        0x202F, 0x202F,
        0x205F, 0x205F,
        0x3000, 0x3000);

    /// <summary>
    /// Returns the code points of ripgrep's \w.
    /// </summary>
    public static CodePointSet Word => _word.Value;

    /// <summary>
    /// Returns true and the set for an ASCII class name, like "alpha" in [[:alpha:]].
    /// </summary>
    public static bool TryGetPosixClass(string name, out CodePointSet set)
    {
        if (PosixClasses.TryGetValue(name, out var posixClass))
        {
            set = posixClass;
            return true;
        }

        set = CodePointSet.Empty;
        return false;
    }

    /// <summary>
    /// Returns true and the set for the name in \p{...}: a general category, Any, ASCII or Assigned. Names match
    /// loosely, the same as in ripgrep, so "Lu", "lu", "Uppercase Letter" and "gc=Lu" all name one category.
    /// </summary>
    public static bool TryGetProperty(string name, out CodePointSet set)
    {
        set = CodePointSet.Empty;

        var isNegated = false;
        var value = name;
        var separatorIndex = name.IndexOfAny(['=', ':']);
        if (separatorIndex >= 0)
        {
            var key = name.Substring(0, separatorIndex);
            if (key.EndsWith('!'))
            {
                isNegated = true;
                key = key.Substring(0, key.Length - 1);
            }

            var normalizedKey = NormalizePropertyName(key);
            if (normalizedKey != "gc"
                && normalizedKey != "generalcategory")
            {
                return false;
            }
            value = name.Substring(separatorIndex + 1);
        }

        var normalizedValue = NormalizePropertyName(value);
        CodePointSet? property = null;
        if (GeneralCategories.TryGetValue(normalizedValue, out var categories))
        {
            var builder = new CodePointSet.Builder();
            foreach (var category in categories)
            {
                builder.Add(Category(category));
            }
            property = builder.ToSet();
        }
        else if (separatorIndex < 0)
        {
            property = normalizedValue switch
            {
                "any" => CodePointSet.All,
                "ascii" => Ranges(0x00, 0x7F),
                "assigned" => Category(UnicodeCategory.OtherNotAssigned).Complement(),
                _ => null,
            };
        }

        if (property is null)
        {
            return false;
        }

        set = isNegated ? property.Complement() : property;
        return true;
    }

    /// <summary>
    /// Adds to a set every code point that ripgrep's (?i) matches with one of the set's members.
    /// </summary>
    public static CodePointSet CaseFold(CodePointSet set)
    {
        var table = _caseFoldTable.Value;

        var builder = new CodePointSet.Builder();
        builder.Add(set);
        var foldedSets = new HashSet<int>();
        for (var index = 0; index < table.Members.Length; index++)
        {
            var setIndex = table.SetIndexes[index];
            if (!foldedSets.Contains(setIndex)
                && set.Contains(table.Members[index]))
            {
                foldedSets.Add(setIndex);
                foreach (var member in table.Sets[setIndex])
                {
                    builder.Add(member, member);
                }
            }
        }

        return builder.ToSet();
    }

    private static CodePointSet Category(UnicodeCategory category)
    {
        return _categories.Value[(int)category];
    }

    private static CodePointSet Ranges(params int[] bounds)
    {
        return CodePointSet.FromRangePairs(bounds);
    }

    // Matches property names loosely: letter case, spaces, '_', '-' and a leading "is" make no difference.
    private static string NormalizePropertyName(string name)
    {
        var characters = name
            .Where(character => character != ' '
                && character != '_'
                && character != '-'
                && !char.IsWhiteSpace(character))
            .Select(char.ToLowerInvariant)
            .ToArray();
        var normalized = new string(characters);

        if (normalized.StartsWith("is")
            && normalized.Length > 2)
        {
            normalized = normalized.Substring(2);
        }

        return normalized;
    }

    private static CodePointSet[] BuildCategories()
    {
        var builders = new CodePointSet.Builder[(int)UnicodeCategory.OtherNotAssigned + 1];
        for (var index = 0; index < builders.Length; index++)
        {
            builders[index] = new CodePointSet.Builder();
        }

        var rangeStart = 0;
        var rangeCategory = CharUnicodeInfo.GetUnicodeCategory(0);
        for (var codePoint = 1; codePoint <= CodePointSet.MaxCodePoint + 1; codePoint++)
        {
            var category = codePoint <= CodePointSet.MaxCodePoint
                ? CharUnicodeInfo.GetUnicodeCategory(codePoint)
                : UnicodeCategory.OtherNotAssigned;
            if (category == rangeCategory
                && codePoint <= CodePointSet.MaxCodePoint)
            {
                continue;
            }

            builders[(int)rangeCategory].Add(rangeStart, codePoint - 1);
            rangeStart = codePoint;
            rangeCategory = category;
        }

        return builders
            .Select(builder => builder.ToSet())
            .ToArray();
    }

    private sealed record CaseFoldTable(int[] Members, int[] SetIndexes, int[][] Sets);

    private static CaseFoldTable BuildCaseFoldTable()
    {
        var table = RipgrepUnicodeTables.CaseFoldSets;

        var sets = new List<int[]>();
        var members = new List<(int Member, int SetIndex)>();
        var index = 0;
        while (index < table.Length)
        {
            var size = table[index];
            var set = table.Slice(index + 1, size).ToArray();
            foreach (var member in set)
            {
                members.Add((member, sets.Count));
            }
            sets.Add(set);
            index += size + 1;
        }

        members.Sort();

        var memberCodePoints = members
            .Select(member => member.Member)
            .ToArray();
        var setIndexes = members
            .Select(member => member.SetIndex)
            .ToArray();

        return new CaseFoldTable(memberCodePoints, setIndexes, sets.ToArray());
    }

    private static IReadOnlyDictionary<string, UnicodeCategory[]> BuildGeneralCategoryNames()
    {
        UnicodeCategory[] other =
        [
            UnicodeCategory.Control,
            UnicodeCategory.Format,
            UnicodeCategory.OtherNotAssigned,
            UnicodeCategory.PrivateUse,
        ];
        UnicodeCategory[] letter =
        [
            UnicodeCategory.UppercaseLetter,
            UnicodeCategory.LowercaseLetter,
            UnicodeCategory.TitlecaseLetter,
            UnicodeCategory.ModifierLetter,
            UnicodeCategory.OtherLetter,
        ];
        UnicodeCategory[] casedLetter =
        [
            UnicodeCategory.UppercaseLetter,
            UnicodeCategory.LowercaseLetter,
            UnicodeCategory.TitlecaseLetter,
        ];
        UnicodeCategory[] mark =
        [
            UnicodeCategory.NonSpacingMark,
            UnicodeCategory.SpacingCombiningMark,
            UnicodeCategory.EnclosingMark,
        ];
        UnicodeCategory[] number =
        [
            UnicodeCategory.DecimalDigitNumber,
            UnicodeCategory.LetterNumber,
            UnicodeCategory.OtherNumber,
        ];
        UnicodeCategory[] punctuation =
        [
            UnicodeCategory.ConnectorPunctuation,
            UnicodeCategory.DashPunctuation,
            UnicodeCategory.OpenPunctuation,
            UnicodeCategory.ClosePunctuation,
            UnicodeCategory.InitialQuotePunctuation,
            UnicodeCategory.FinalQuotePunctuation,
            UnicodeCategory.OtherPunctuation,
        ];
        UnicodeCategory[] symbol =
        [
            UnicodeCategory.MathSymbol,
            UnicodeCategory.CurrencySymbol,
            UnicodeCategory.ModifierSymbol,
            UnicodeCategory.OtherSymbol,
        ];
        UnicodeCategory[] separator =
        [
            UnicodeCategory.SpaceSeparator,
            UnicodeCategory.LineSeparator,
            UnicodeCategory.ParagraphSeparator,
        ];

        var names = new Dictionary<string, UnicodeCategory[]>(StringComparer.Ordinal);
        void Add(UnicodeCategory[] categories, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                names[alias] = categories;
            }
        }

        Add(other, "c", "other");
        Add([UnicodeCategory.Control], "cc", "control", "cntrl");
        Add([UnicodeCategory.Format], "cf", "format");
        Add([UnicodeCategory.OtherNotAssigned], "cn", "unassigned");
        Add([UnicodeCategory.PrivateUse], "co", "privateuse");
        Add(letter, "l", "letter");
        Add(casedLetter, "lc", "casedletter");
        Add([UnicodeCategory.LowercaseLetter], "ll", "lowercaseletter");
        Add([UnicodeCategory.ModifierLetter], "lm", "modifierletter");
        Add([UnicodeCategory.OtherLetter], "lo", "otherletter");
        Add([UnicodeCategory.TitlecaseLetter], "lt", "titlecaseletter");
        Add([UnicodeCategory.UppercaseLetter], "lu", "uppercaseletter");
        Add(mark, "m", "mark", "combiningmark");
        Add([UnicodeCategory.SpacingCombiningMark], "mc", "spacingmark");
        Add([UnicodeCategory.EnclosingMark], "me", "enclosingmark");
        Add([UnicodeCategory.NonSpacingMark], "mn", "nonspacingmark");
        Add(number, "n", "number");
        Add([UnicodeCategory.DecimalDigitNumber], "nd", "decimalnumber", "digit");
        Add([UnicodeCategory.LetterNumber], "nl", "letternumber");
        Add([UnicodeCategory.OtherNumber], "no", "othernumber");
        Add(punctuation, "p", "punctuation", "punct");
        Add([UnicodeCategory.ConnectorPunctuation], "pc", "connectorpunctuation");
        Add([UnicodeCategory.DashPunctuation], "pd", "dashpunctuation");
        Add([UnicodeCategory.ClosePunctuation], "pe", "closepunctuation");
        Add([UnicodeCategory.FinalQuotePunctuation], "pf", "finalpunctuation");
        Add([UnicodeCategory.InitialQuotePunctuation], "pi", "initialpunctuation");
        Add([UnicodeCategory.OtherPunctuation], "po", "otherpunctuation");
        Add([UnicodeCategory.OpenPunctuation], "ps", "openpunctuation");
        Add(symbol, "s", "symbol");
        Add([UnicodeCategory.CurrencySymbol], "sc", "currencysymbol");
        Add([UnicodeCategory.ModifierSymbol], "sk", "modifiersymbol");
        Add([UnicodeCategory.MathSymbol], "sm", "mathsymbol");
        Add([UnicodeCategory.OtherSymbol], "so", "othersymbol");
        Add(separator, "z", "separator");
        Add([UnicodeCategory.LineSeparator], "zl", "lineseparator");
        Add([UnicodeCategory.ParagraphSeparator], "zp", "paragraphseparator");
        Add([UnicodeCategory.SpaceSeparator], "zs", "spaceseparator");

        return names;
    }
}
