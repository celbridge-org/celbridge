using System.Security.Cryptography;
using System.Text;

namespace Celbridge.Tests.Patterns;

/// <summary>
/// Names the part of a row that Celbridge refuses. ripgrep gives an answer for that part.
/// </summary>
public enum SearchPatternRefusal
{
    None,
    Pattern,
    Replacement,
}

/// <summary>
/// A row of the comparison table. Flags are ripgrep's: -F for literal, -i to ignore letter case, -w for whole words
/// and -U for multiline. ExpectedMatches and ExpectedReplaced are set on a row where Celbridge differs from ripgrep on
/// purpose, and give Celbridge's answer.
/// </summary>
public sealed record SearchPatternCase(
    string Name,
    string Pattern,
    string Text,
    string Flags,
    string? Replacement,
    SearchPatternRefusal Refusal,
    string? ExpectedMatches,
    string? ExpectedReplaced)
{
    public IReadOnlyList<string> FlagList => Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public SearchPatternOptions Options => new(
        Literal: FlagList.Contains("-F"),
        MatchCase: !FlagList.Contains("-i"),
        WholeWord: FlagList.Contains("-w"),
        Multiline: FlagList.Contains("-U"));
}

/// <summary>
/// ripgrep's answer for a row: a refusal, or the matches and the replaced text. Matches lists each match as
/// "start:length" in UTF-16 characters, separated by spaces.
/// </summary>
public sealed record SearchPatternAnswer(bool IsRefused, string Matches, string? Replaced)
{
    public static SearchPatternAnswer Refused { get; } = new(true, string.Empty, null);
}

/// <summary>
/// The rows for comparing SearchPattern with ripgrep. Each row has a pattern, ripgrep's flags, a text and an optional
/// replacement.
/// </summary>
public static class SearchPatternComparisonCases
{
    /// <summary>
    /// A text with every ASCII letter and the letters that Unicode case folding joins to them, and a few more.
    /// </summary>
    public const string CaseFoldText = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ \u017F\u212A\u212B\u0131\u0130\u00B5\u00DF\u1E9E\u01C5\u2126\u03C3\u03C2\u03A3\n";

    public static IReadOnlyList<SearchPatternCase> Rows { get; } =
    [
        // Plain text.
        Row("literal", "foo", "a foo b foo\n", "-F"),
        Row("literal_never_overlaps", "aa", "aaaaa\n", "-F"),
        Row("literal_metacharacters", "a.b*c", "a.b*c axbc\n", "-F"),
        Row("literal_ignore_case", "HeLLo", "hello HELLO hElLo\n", "-F -i"),
        Row("literal_ignore_case_long_s", "s", "sS\u017F\n", "-F -i"),
        Row("literal_ignore_case_kelvin", "k", "kK\u212A\n", "-F -i"),
        Row("literal_whole_word", "foo", "foo food _foo foo_ (foo) foo\n", "-F -w"),
        Row("literal_whole_word_punctuation", "-foo", "a -foo b x-foo\n", "-F -w"),
        Row("literal_whole_word_after_a_refused_candidate", "aa", "aaa aa\n", "-F -w"),
        Row("literal_whole_word_ignore_case", "FOO", "foo Foo food\n", "-F -i -w"),
        Row("literal_whole_word_devanagari", "\u0915", "\u0915 \u0915\u093F\n", "-F -w"),
        Row("literal_above_ffff", "\U0001F600", "a\U0001F600b\U0001F600\n", "-F"),
        Row("literal_crlf", "line", "line\r\nline\r\n", "-F"),
        Row("literal_replacement", "foo", "foo bar foo\n", "-F", replacement: "baz"),
        Row("literal_multiline", "a\nb", "a\nb\nc\n", "-F -U"),
        Row("literal_line_break_without_multiline", "a\nb", "a\nb\n", "-F", refusal: SearchPatternRefusal.Pattern),

        // Literals, alternation and repetition.
        Row("regex_literal", "foo", "a foo b foo\n"),
        Row("alternation", "foo|bar", "foo bar baz\n"),
        Row("alternation_takes_the_first_branch", "a|ab", "ab\n"),
        Row("alternation_with_empty_branch", "a|", "ba\n"),
        Row("groups_take_the_first_branch_that_matches", "(a|ab)(c|bcd)", "abcd\n", replacement: "[$1][$2]"),
        Row("greedy_star", "<.*>", "<a><b>\n"),
        Row("lazy_star", "<.*?>", "<a><b>\n"),
        Row("lazy_plus", "a+?", "aaa\n"),
        Row("counted_exact", "a{2}", "aaaaa\n"),
        Row("counted_at_least", "a{2,}", "aaaaa\n"),
        Row("counted_range", "a{2,3}", "aaaaa\n"),
        Row("counted_lazy", "a{2,}?", "aaaa\n"),
        Row("count_with_spaces", "a{ 2 , 3 }", "aaaa\n"),
        Row("space_before_a_count", "a {2}", "aa a  \n"),
        Row("count_with_a_space_for_its_maximum", "a{2, }", "aa\n"),
        Row("nested_repetition", "a**", "aa\n"),
        Row("repeated_count", "a{2}{3}", "aaaaaaa\n"),
        Row("repeated_group", "(?:ab)+", "ababa\n"),
        Row("swap_greed", "(?U)a+", "aaa\n"),
        Row("swap_greed_back", "(?U)a+?", "aaa\n"),
        Row("repeated_assertion", "^*a", "a\n"),
        Row("closing_brackets_are_literal", "a]b}", "a]b}\n"),
        Row("long_line_runs_in_linear_time", "(a+)+$", new string('a', 2000) + "!\n"),

        // The dot and line ends.
        Row("dot", "a.c", "abc a.c ac\n"),
        Row("dot_skips_carriage_return", "a.c", "a\rc\n"),
        Row("dot_all", "(?s)a.c", "abc\n"),
        Row("dot_above_ffff", "a.b", "a\U0001F600b\n"),
        Row("dot_matches_a_whole_character", "^.$", "\U0001F600\n"),
        Row("two_dots_above_ffff", ".{2}", "\U0001F600\U0001F600a\n"),
        Row("start_of_line", "^foo", "foo foo\nfoo\n"),
        Row("end_of_line", "foo$", "foo foo\nfoo\n"),
        Row("empty_line", "^$", "a\n\nb\n"),
        Row("end_of_line_before_crlf", "foo$", "foo\r\nfoo\r\n"),
        Row("lone_carriage_return_ends_a_line", "a$", "a\rb\n"),
        Row("lone_carriage_return_starts_a_line", "^b", "a\rb\n"),
        Row("start_of_text_refused", @"\Afoo", "foo\nfoo\n", refusal: SearchPatternRefusal.Pattern),
        Row("end_of_text_refused", @"foo\z", "foo\nfoo\n", refusal: SearchPatternRefusal.Pattern),
        Row("multi_line_flag_off_refused", "(?-m)^a", "b\na\n", refusal: SearchPatternRefusal.Pattern),
        Row("multi_line_flag_on", "(?m)^a", "b\na\n"),

        // Empty matches.
        Row("empty_matches", "x*", "abc\n"),
        Row("empty_match_after_a_match", "a*", "ab\n"),
        Row("empty_match_replacement", "a*", "ab\n", replacement: "X"),

        // Word boundaries.
        Row("word_boundary", @"\bfoo\b", "foo food (foo) _foo\n"),
        Row("not_word_boundary", @"\Boo\B", "foo fooo\n"),
        Row("word_start_refused", @"\<foo", "foo\n", refusal: SearchPatternRefusal.Pattern),
        Row("word_end_refused", @"foo\>", "foo\n", refusal: SearchPatternRefusal.Pattern),
        Row("word_start_half_refused", @"\b{start}foo", "foo\n", refusal: SearchPatternRefusal.Pattern),

        // Classes.
        Row("class_range", "[a-c]+", "abcd\n"),
        Row("negated_class", "[^a-c]+", "abcd\n"),
        Row("bracket_first_is_literal", "[]a]+", "]a\n"),
        Row("negated_bracket_first_is_literal", "[^]a]+", "]ab\n"),
        Row("dash_last_is_literal", "[a-]+", "-a\n"),
        Row("dashes_first_are_literal", "[--a]+", "-a\n"),
        Row("escaped_dash", @"[a\-z]+", "a-zb\n"),
        Row("escaped_brackets", @"[\]\[\^]+", "][^\n"),
        Row("nested_class", "[a[bc]]+", "abcd\n"),
        Row("class_above_ffff", "[\U0001F600]", "\U0001F600\n"),
        Row("class_range_above_ffff", @"[\x{1F600}-\x{1F64F}]+", "\U0001F600\U0001F64F\U0001F650\n"),
        Row("negated_class_above_ffff", "[^a]", "\U0001F600\n"),
        Row("repeated_character_above_ffff", "\U0001F600+", "\U0001F600\U0001F600\n"),
        Row("class_with_line_break", @"[a\n]", "a\n"),
        Row("class_range_over_line_breaks", @"[\x0A-\x0D]", "a\vb\rc\n"),
        Row("posix_alpha", "[[:alpha:]]+", "ab1\u00E9\n"),
        Row("posix_negated", "[[:^alpha:]]+", "ab1\u00E9\n"),
        Row("posix_pair", "[[:digit:][:upper:]]+", "a1B2c\n"),
        Row("posix_ignore_case", "(?i)[[:upper:]]+", "aB\u017F\u212A\n"),
        Row("posix_unknown_name_is_a_nested_class", "[[:foo:]]", "f:o\n"),
        Row("posix_outside_brackets", "[:alpha:]", "a:\n"),
        Row("intersection", "[a-z&&[^aeiou]]+", "banana\n"),
        Row("difference", "[a-z--aeiou]+", "banana\n"),
        Row("symmetric_difference", "[a-c~~b-d]+", "abcde\n"),
        Row("intersection_with_nothing", "[a&&]", "a\n"),
        Row("operators_left_to_right", "[a-z&&b-y--c]+", "abcd\n"),
        Row("class_operator_ignore_case", "(?i)[a-z&&[^k]]+", "kK\u212Aab\n"),

        // Perl and Unicode classes.
        Row("digits", @"\d+", "a12\u0663\n"),
        Row("not_digits", @"\D+", "a12b\n"),
        Row("spaces", @"\s+", "a \t\u00A0b\n"),
        Row("not_spaces", @"\S+", "a  b\n"),
        Row("words", @"\w+", "hello_world \u0915\u093F\u0924\u093E\u092C \U0001D400\U0001D401 x\n"),
        Row("not_words", @"\W+", "a, b!\n"),
        Row("letters", @"\pL+", "ab\u00E9\U0001D400 1\n"),
        Row("uppercase_letters", @"\p{Lu}+", "AbC\u00C9\n"),
        Row("not_letters", @"\PL+", "ab12c\n"),
        Row("numbers", @"\pN+", "1\u00BD\u2163 x\n"),
        Row("loose_property_name", @"\p{Uppercase Letter}+", "AbC\n"),
        Row("general_category_key", @"\p{gc=Lu}+", "AbC\n"),
        Row("general_category_not_equal", @"\p{gc!=Lu}+", "AbC\n"),
        Row("any", @"\p{Any}", "a\U0001F600\n"),
        Row("ascii", @"\p{ASCII}+", "ab\u00E9c\n"),
        Row("assigned", @"\p{Assigned}+", "ab\n"),
        Row("script_refused", @"\p{Greek}", "\u03B1\n", refusal: SearchPatternRefusal.Pattern),
        Row("binary_property_refused", @"\p{Alphabetic}", "a\n", refusal: SearchPatternRefusal.Pattern),
        Row("words_above_ffff", @"\w+", "\U0001D400\U0001D401 x\n"),

        // Escapes.
        Row("hex_two_digits", @"\x41", "A\n"),
        Row("hex_braced", @"\x{1F600}", "\U0001F600\n"),
        Row("unicode_four_digits", @"\u0041", "A\n"),
        Row("unicode_braced", @"\u{41}", "A\n"),
        Row("unicode_eight_digits", @"\U00000041", "A\n"),
        Row("tab", @"\t", "a\tb\n"),
        Row("escaped_punctuation", @"\#\&\~\-", "#&~-\n"),
        Row("escaped_space", @"a\ b", "a b\n"),

        // Letter case.
        Row("ignore_case", "(?i)hello", "HeLLo\n"),
        Row("ignore_case_class", "(?i)[a-z]+", "ABC\n"),
        Row("ignore_case_negated_class", "(?i)[^k]", "kK\u212A\n"),
        Row("ignore_case_negated_property", @"(?i)\P{Lu}", "aA1\n"),
        Row("ignore_case_above_ffff", "(?i)\U00010400", "\U00010428\n"),
        Row("ignore_case_sharp_s", "(?i)\u00DF", "\u1E9E\n"),
        Row("ignore_case_titlecase", "(?i)\u01C5", "\u01C4\u01C5\u01C6\n"),
        Row("ignore_case_dotless_i", "(?i)i", "iI\u0131\u0130\n"),
        Row("ignore_case_sigma", "(?i)\u03C3", "\u03C3\u03C2\u03A3\n"),
        Row("ignore_case_flag", "foo", "FOO foo\n", "-i"),
        Row("scoped_ignore_case", "(?i:a)b", "Ab AB\n"),
        Row("ignore_case_from_the_middle", "a(?i)b", "aB AB\n"),
        Row("ignore_case_turned_off", "(?i)a(?-i)b", "AB Ab\n"),
        .. AsciiLetterRows(),

        // Flags.
        Row("verbose", "(?x) a b ", "ab\n"),
        Row("verbose_comment", "(?x) a b # comment", "ab\n"),
        Row("verbose_line_break", "(?x)a\nb", "ab\n"),
        Row("verbose_class", "(?x)[a b]+", "a b\n"),
        Row("verbose_escaped_space", @"(?x)a\ b", "a b\n"),
        Row("verbose_count", "(?x)a{ 2 }", "aa\n"),
        Row("flag_group_with_negation", "(?i-s:a.)", "A\n"),
        Row("crlf_flag", "(?R)a$", "a\r\n"),
        Row("unicode_flag", "(?u)a", "a\n"),
        Row("unicode_flag_off_refused", @"(?-u)\w", "a\n", refusal: SearchPatternRefusal.Pattern),
        Row("crlf_flag_off_refused", "(?-R)a", "a\n", refusal: SearchPatternRefusal.Pattern),

        // Patterns that ripgrep refuses.
        Row("lookahead", "a(?=b)", "ab\n"),
        Row("negative_lookahead", "a(?!b)", "ab\n"),
        Row("lookbehind", "(?<=a)b", "ab\n"),
        Row("negative_lookbehind", "(?<!a)b", "ab\n"),
        Row("backreference", @"(a)\1", "aa\n"),
        Row("brace_without_repetition", "foo{bar", "foo{bar\n"),
        Row("count_without_a_minimum", "a{,3}", "aaa\n"),
        Row("count_range_backwards", "x{2,1}", "xx\n"),
        Row("duplicate_group_name", "(?<n>a)(?<n>b)", "ab\n"),
        Row("group_name_starting_with_a_digit", "(?P<1a>x)", "x\n"),
        Row("unknown_escape", @"\e", "e\n"),
        Row("range_to_a_class", @"[a-\d]", "a\n"),
        Row("empty_flag_group", "(?)", "a\n"),
        Row("dangling_flag_negation", "(?i-)", "a\n"),
        Row("repeated_flag_negation", "(?--i)", "a\n"),
        Row("surrogate_property", @"\p{Cs}", "a\n"),
        Row("negated_property_name", @"\p{^L}", "a\n"),
        Row("unclosed_group", "(abc", "abc\n"),
        Row("unopened_group", "abc)", "abc\n"),
        Row("unclosed_class", "[abc", "abc\n"),
        Row("surrogate_escape", @"\x{D800}", "a\n"),
        Row("escape_above_unicode", @"\x{110000}", "a\n"),
        Row("bad_hex_digit", @"\xG1", "a\n"),
        Row("repetition_of_nothing", "*a", "a\n"),
        Row("repetition_after_alternation", "a|*", "a\n"),
        Row("unknown_flag", "(?q)a", "a\n"),
        Row("line_break_escape", @"a\nb", "a\nb\n"),
        Row("line_break_hex", @"\x0A", "a\n"),
        Row("class_of_a_line_break", @"[\n]", "a\n"),
        Row("carriage_return_escape", @"a\r", "a\rb\n"),

        // Repetitions too large for the linear-time engine.
        Row("huge_count_refused", "a{100000}", "a\n", refusal: SearchPatternRefusal.Pattern),

        // Whole words with a regex.
        Row("whole_word_regex", "foo", "foo food (foo)\n", "-w"),
        Row("whole_word_takes_a_later_branch", "foo|foobar", "foobar foo\n", "-w"),
        Row("whole_word_word_class", @"\w+", "_a b\n", "-w"),
        Row("whole_word_no_match", "a+", "aab b\n", "-w"),
        Row("whole_word_ignore_case", "FOO", "foo Foo food\n", "-w -i"),
        Row("whole_word_punctuation_refused", "-foo", "a -foo b\n", "-w", refusal: SearchPatternRefusal.Pattern),
        Row("whole_word_empty_refused", "a*", "a b\n", "-w", refusal: SearchPatternRefusal.Pattern),
        Row("whole_word_dot_refused", ".+", "a b\n", "-w", refusal: SearchPatternRefusal.Pattern),

        // Multiline.
        Row("multiline_line_break", @"a\nb", "a\nb\nc\n", "-U"),
        Row("multiline_start_of_line", "^b", "a\nb\n", "-U"),
        Row("multiline_end_of_line", "a$", "a\nb\n", "-U"),
        Row("multiline_dot_stops_at_line_break", "a.b", "a\nb\n", "-U"),
        Row("multiline_dot_all", "(?s)a.b", "a\nb\n", "-U"),
        Row("multiline_negated_class", "[^x]+", "a\nb\n", "-U"),
        Row("multiline_spaces", @"a\s+b", "a \n b\n", "-U"),
        Row("multiline_end_of_line_before_crlf", "foo$", "foo\r\nbar\r\n", "-U"),
        Row("multiline_start_of_line_after_crlf", "^bar", "foo\r\nbar\r\n", "-U"),
        Row("multiline_spaces_over_crlf", @"a\s+b", "a\r\nb\r\n", "-U"),
        Row("multiline_replacement", @"a\nb", "a\nb\nc\n", "-U", replacement: "X"),
        Row("multiline_carriage_return_refused", @"a\r\nb", "a\r\nb\r\n", "-U", refusal: SearchPatternRefusal.Pattern),

        // Replacements.
        Row("replacement_swaps_groups", @"(\w+) (\w+)", "hello world\n", replacement: "$2 $1"),
        Row("replacement_named_group", @"(?P<first>\w+)", "hello\n", replacement: "<$first>"),
        Row("replacement_braced_name", "(?<x>a)", "a\n", replacement: "${x}y"),
        Row("replacement_dollar_dollar", "(a)", "a\n", replacement: "$$1"),
        Row("replacement_braced_number", "(a)", "a\n", replacement: "${1}b"),
        Row("replacement_number_then_letter_refused", "(a)", "a\n", replacement: "$1b", refusal: SearchPatternRefusal.Replacement),
        Row("replacement_missing_group_refused", "(a)", "a\n", replacement: "$2", refusal: SearchPatternRefusal.Replacement),
        Row("replacement_unmatched_group", "(a)|(b)", "a\n", replacement: "[$2]"),
        Row("replacement_whole_match", "a", "a\n", replacement: "[$0]"),
        Row("replacement_ampersand_is_literal", "(a)", "a\n", replacement: "$&"),
        Row("replacement_empty_braces_are_literal", "(a)", "a\n", replacement: "${}"),
        Row("replacement_dollar_at_end", "(a)", "a\n", replacement: "x$"),
        Row("replacement_dollar_then_space", "(a)", "a\n", replacement: "$ 1"),
        Row("replacement_leading_zero", "(a)", "a\n", replacement: "$01"),
        Row("replacement_non_ascii_name_is_literal", "(?<\u00E9>a)", "a\n", replacement: "${\u00E9}"),
        Row("replacement_ignore_case", "(?i)(foo)", "FOO\n", replacement: "[$1]"),
    ];

    /// <summary>
    /// The rows where Celbridge differs from ripgrep on purpose. Each row gives Celbridge's answer.
    /// </summary>
    public static IReadOnlyList<SearchPatternCase> Differences { get; } =
    [
        // A plain-text replacement is plain text, and ripgrep expands '$' even with -F.
        Difference("literal_replacement_dollar", "foo", "foo\n", "-F", replacement: "[$0]", expectedMatches: "0:3", expectedReplaced: "[$0]\n"),

        // With multiline, '\r\n' reads as one '\n'.
        Difference("multiline_line_break_over_crlf", @"a\nb", "a\r\nb\r\n", "-U", replacement: null, expectedMatches: "0:4", expectedReplaced: null),
        Difference("multiline_class_over_crlf", "a[^x]b", "a\r\nb\r\n", "-U", replacement: null, expectedMatches: "0:4", expectedReplaced: null),
        Difference("literal_multiline_over_crlf", "a\nb", "a\r\nb\r\n", "-F -U", replacement: null, expectedMatches: "0:4", expectedReplaced: null),

        // ripgrep matches bytes, so an empty match can fall between the bytes of a character. Celbridge's falls
        // between characters.
        Difference("empty_matches_between_characters", "x*", "\U0001F600\n", "", replacement: null, expectedMatches: "0:0 2:0", expectedReplaced: null),

        // .NET's \b reads a spacing mark, like Devanagari's vowel sign i, as a non-word character.
        Difference("word_boundary_in_devanagari", @"\b\u0924", "\u0915\u093F\u0924\u093E\u092C\n", "", replacement: null, expectedMatches: "2:1", expectedReplaced: null),
    ];

    /// <summary>
    /// Returns a hash of every row. The answers file stores this hash at the time of recording.
    /// </summary>
    public static string Fingerprint()
    {
        var text = new StringBuilder();
        foreach (var row in Rows.Concat(Differences))
        {
            text.Append(row.Name);
            text.Append('\0');
            text.Append(row.Pattern);
            text.Append('\0');
            text.Append(row.Text);
            text.Append('\0');
            text.Append(row.Flags);
            text.Append('\0');
            text.Append(row.Replacement ?? "\u0001");
            text.Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));

        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    private static SearchPatternCase Row(
        string name,
        string pattern,
        string text,
        string flags = "",
        string? replacement = null,
        SearchPatternRefusal refusal = SearchPatternRefusal.None)
    {
        return new SearchPatternCase(name, pattern, text, flags, replacement, refusal, ExpectedMatches: null, ExpectedReplaced: null);
    }

    private static SearchPatternCase Difference(
        string name,
        string pattern,
        string text,
        string flags,
        string? replacement,
        string expectedMatches,
        string? expectedReplaced)
    {
        return new SearchPatternCase(name, pattern, text, flags, replacement, SearchPatternRefusal.None, expectedMatches, expectedReplaced);
    }

    // One row for each ASCII letter under (?i), over a text with the letters that case folding can join to it.
    private static IEnumerable<SearchPatternCase> AsciiLetterRows()
    {
        for (var letter = 'a'; letter <= 'z'; letter++)
        {
            yield return Row($"ignore_case_letter_{letter}", $"(?i){letter}", CaseFoldText);
        }
    }
}
