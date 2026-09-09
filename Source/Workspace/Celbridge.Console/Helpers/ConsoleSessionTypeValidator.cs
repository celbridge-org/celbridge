using System.Text.RegularExpressions;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Checks that the registered session types can be named in a .console file. A type id becomes a
/// [session.&lt;id&gt;] table name, so an id the format cannot express, or one the format has already taken,
/// produces a document whose table is silently read as something else. Nothing about a single id can catch
/// that on its own, so the whole registered set is checked together.
/// </summary>
public static class ConsoleSessionTypeValidator
{
    // Lowercase, starting with a letter, hyphens allowed. A dot would nest the table ([session.a.b]) and a
    // character outside this set would not be a bare TOML key at all.
    private static readonly Regex TypeIdRegex = new("^[a-z][a-z0-9-]*$", RegexOptions.Compiled);

    /// <summary>
    /// The names the [session] table already defines, which a type id therefore cannot take. The scalar keys
    /// are here too, because a table sharing a name with one of them is a duplicate key rather than a type's
    /// table.
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
    /// Checks every registered session type, reporting the first problem found. A failure is a programming
    /// error in a session type provider rather than anything a user can cause.
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
