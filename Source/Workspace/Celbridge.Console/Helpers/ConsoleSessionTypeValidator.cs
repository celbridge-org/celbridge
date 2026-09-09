using System.Text.RegularExpressions;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Checks that the registered session types can be named in a .console file. A type id becomes a
/// [session.&lt;id&gt;] table name, so it must be spellable as one and must not take a name the format has
/// already defined.
/// </summary>
public static class ConsoleSessionTypeValidator
{
    // The end anchor is \z rather than $, which would also match before a trailing newline and admit an id
    // no table header can spell.
    private static readonly Regex TypeIdRegex = new(@"^[a-z][a-z0-9-]*\z", RegexOptions.Compiled);

    /// <summary>
    /// The names the [session] table already defines, which a type id therefore cannot take.
    /// </summary>
    private static readonly IReadOnlySet<string> ReservedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "type",
        "working_directory",
        "disabled_runners",
        "environment",
        "runner",
        "trigger",
        "shortcut",
    };

    /// <summary>
    /// Checks every registered session type, reporting the first problem found.
    /// </summary>
    public static Result Validate(IReadOnlyList<ConsoleSessionType> sessionTypes)
    {
        var seenTypeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sessionType in sessionTypes)
        {
            var typeId = sessionType.TypeId;

            if (string.IsNullOrWhiteSpace(typeId))
            {
                return Result.Fail("A console session type declares an empty type id.");
            }

            if (!TypeIdRegex.IsMatch(typeId))
            {
                return Result.Fail(
                    $"Console session type '{typeId}' is not a valid type id. Expected lowercase letters, " +
                    "digits and hyphens, starting with a letter.");
            }

            if (ReservedNames.Contains(typeId))
            {
                return Result.Fail(
                    $"Console session type '{typeId}' takes a name the [session] table already defines.");
            }

            if (!seenTypeIds.Add(typeId))
            {
                return Result.Fail($"Console session type '{typeId}' is registered more than once.");
            }
        }

        return Result.Ok();
    }
}
