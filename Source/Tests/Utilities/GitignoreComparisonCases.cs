using System.Security.Cryptography;
using System.Text;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// A path in the comparison tree. Listed is the path from the Tree list, which ends in '/' for a folder.
/// </summary>
public sealed record GitignoreTreeEntry(string Listed, string Path, bool IsFolder);

/// <summary>
/// The tree of files and folders, and the gitignore lists, for comparing GitignoreMatcher with git.
/// </summary>
public static class GitignoreComparisonCases
{
    /// <summary>
    /// The files and folders made for git to check. A folder ends in '/' and comes before the paths inside it. No two
    /// names in a folder differ only by letter case, since the Windows and macOS file systems ignore letter case.
    /// </summary>
    public static IReadOnlyList<string> Tree { get; } =
    [
        "README.md",
        "!important.txt",
        "#notes.txt",
        ".hidden",
        ".config/",
        ".config/settings.json",
        "1.log",
        "_.log",
        "a.log",
        "a/",
        "a/b/",
        "a/b/c.txt",
        "a/x/",
        "a/x/b/",
        "a/x/b/c.txt",
        "a/x/y/",
        "a/x/y/b/",
        "a/x/y/b/c.txt",
        "a/xb/",
        "a/xb/c.txt",
        "build/",
        "build/out.o",
        "build/sub/",
        "build/sub/deep.o",
        "cafe.txt",
        "café.txt",
        "dash-name.txt",
        "data [1].csv",
        "data1.csv",
        "data2.csv",
        "dataA.csv",
        "dataz.csv",
        "dir.d/",
        "dir.d/inner",
        "docs/",
        "docs/api/",
        "docs/api/index.md",
        "docs/api/v2/",
        "docs/api/v2/index.md",
        "docs/draft.md",
        "docs/guide.md",
        "foo/",
        "foo/bar",
        "foobar",
        "fooX/",
        "fooX/bar",
        "fooX/Y/",
        "fooX/Y/bar",
        "fooXbar",
        "logs/",
        "logs/app.log",
        "logs/important/",
        "logs/important/app.log",
        "logs/keep.log",
        "logs/old/",
        "logs/old/app.log",
        "mixed-lower/",
        "mixed-lower/case.txt",
        "Mixed/",
        "Mixed/Case.TXT",
        "my file.txt",
        "name#hash.txt",
        "src/",
        "src/app.js",
        "src/app.min.js",
        "src/build/",
        "src/build/gen.c",
        "src/lib/",
        "src/lib/build",
        "src/lib/util.c",
        "src/lib/util.h",
        "src/main.c",
        "under_score.txt",
        "x/",
        "x/a.b/",
        "x/a.b/file",
        "[brackets]/",
        "[brackets]/file.txt",
    ];

    /// <summary>
    /// The gitignore lists, by name. Git and the matcher read each list on its own.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        // A pattern with no slash matches a name at any depth.
        ["names"] = ["*.o", "build", ".hidden"],

        // A trailing slash matches folders only.
        ["folders_only"] = ["build/", ".config/"],

        // A slash at the start or in the middle makes a pattern relative to the base folder.
        ["anchored"] = ["/README.md", "docs/draft.md", "/src/*.js", "a/b", "src/build/"],
        ["anchored_double_star"] = ["/**", "!docs"],
        ["root_star"] = ["/*", "!/src"],
        ["single_star"] = ["src/*", "*/util.h"],

        ["leading_double_star"] = ["**/c.txt", "**/old"],
        ["middle_double_star"] = ["a/**/b"],
        ["trailing_double_star"] = ["docs/**"],
        ["double_star_alone"] = ["**"],
        ["every_folder"] = ["**/"],
        ["folder_patterns"] = ["a/**/", "*/sub/", "/logs/"],

        // A '**' glued to other characters matches within a name.
        ["double_star_after_literal"] = ["foo**/bar"],
        ["double_star_after_wildcard"] = ["f*o**/bar"],
        ["double_star_in_name"] = ["**foo", "fooX**", "src/app**"],

        ["star_alone"] = ["*", "!*.txt"],
        ["question_mark"] = ["data?.csv", "?.log"],
        ["dots"] = [".*", "*.b", "dir.d/"],

        ["classes"] = ["data[12].csv", "[[]brackets]"],
        ["negated_classes"] = ["data[!1].csv", "data[^2A].csv"],

        // A range written in reverse matches its start only.
        ["ranges"] = ["data[0-9].csv", "data[z-a].csv", "[a-]*.log", "[_-a].log"],
        ["posix_classes"] = ["[[:digit:]].log", "data[[:alpha:]].csv", "[[:punct:]]*.txt"],

        // A ']' first in a class is a literal, and '[:' without ':]' reads as a literal '['.
        ["class_edge_cases"] = ["[]d]ash-name.txt", "[!]]ata1.csv", "[[:d]ata2.csv"],

        // A slash inside a class makes the pattern relative to the base folder.
        ["class_with_slash"] = ["[a/]"],

        ["escapes"] = [@"\!important.txt", @"\#notes.txt", @"data \[1\].csv", @"name\#hash.txt", @"my\ file.txt"],
        ["escaped_brackets"] = [@"\[brackets]", @"[\[]brackets]/file.txt", @"data[\]1].csv", @"dash[\-]name.txt"],

        // Git drops the spaces at the end of a line, except a space written with '\' before it.
        ["trailing_spaces"] = ["my file.txt   ", @"data1.csv\ ", @"data2.csv\  "],

        ["comments_and_blanks"] = ["# A comment", string.Empty, "#notes.txt", "README.md"],

        // Each of these lines matches nothing.
        ["degenerate"] = ["/", "!", "//foo", "foo//"],

        ["negation"] = ["*.log", "!keep.log"],

        // A path inside an excluded folder cannot be put back.
        ["negation_inside_excluded_folder"] = ["logs/", "!logs/keep.log"],
        ["negation_of_folder"] = ["logs/*", "!logs/important/"],
        ["nested_negation"] = ["docs/api/*", "!docs/api/v2", "docs/api/v2/*", "!docs/api/v2/index.md"],
        ["everything_except"] = ["*", "!*/", "!*.txt"],
        ["last_match_wins"] = ["!*.md", "*.md", "!docs/guide.md"],

        // Letter case is exact.
        ["letter_case"] = ["mixed/", "*.txt"],

        ["non_ascii"] = ["caf*.txt", "café.txt"],
    };

    /// <summary>
    /// The lists where the matcher differs from git on purpose, by name. Git reads '?' and a class as matching one
    /// byte of a name's UTF-8, and the matcher reads one character, so only the matcher matches 'café.txt'.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DifferenceLists { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["question_mark_outside_ascii"] = ["caf?.txt"],
        ["class_outside_ascii"] = ["caf[é].txt"],
    };

    /// <summary>
    /// The tree's paths, in order.
    /// </summary>
    public static IReadOnlyList<GitignoreTreeEntry> Entries { get; } = Tree
        .Select(listed => new GitignoreTreeEntry(listed, listed.TrimEnd('/'), listed.EndsWith('/')))
        .ToList();

    /// <summary>
    /// Returns a hash of the tree and every list. The answers file stores this hash at the time of recording.
    /// </summary>
    public static string Fingerprint()
    {
        var text = new StringBuilder();
        foreach (var listed in Tree)
        {
            text.Append(listed);
            text.Append('\n');
        }

        var lists = Lists.Concat(DifferenceLists).OrderBy(list => list.Key, StringComparer.Ordinal);
        foreach (var list in lists)
        {
            text.Append('\0');
            text.Append(list.Key);
            foreach (var line in list.Value)
            {
                text.Append('\n');
                text.Append(line);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));

        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }
}
