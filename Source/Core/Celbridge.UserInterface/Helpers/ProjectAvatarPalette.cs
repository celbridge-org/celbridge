using System.Text;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Derives the initials and the colour of a project's avatar from the project name, so a project presents
/// the same avatar everywhere it is listed and neighbouring projects are easy to tell apart.
/// </summary>
public static class ProjectAvatarPalette
{
    // Twelve hues that all carry white text at a contrast ratio above 3.5:1, and are light enough to read
    // against a dark panel and dark enough to read against a light one, so the tile needs no per-theme
    // adjustment. Tune the set here rather than at the call site.
    private static readonly string[] TileColors = new[]
    {
        "#C33131",
        "#C05621",
        "#A16207",
        "#4D7C0F",
        "#15803D",
        "#0F766E",
        "#0E7490",
        "#0369A1",
        "#2352C8",
        "#5B35C4",
        "#9333A8",
        "#BE1D5E"
    };

    private const int MaxInitials = 2;
    private const string UnknownInitials = "?";

    /// <summary>
    /// Returns up to two uppercase initials, one from each of the first two words in the project name, or a
    /// question mark when the name holds no letters or digits.
    /// </summary>
    public static string GetInitials(string projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            return UnknownInitials;
        }

        var initials = new StringBuilder();
        var isWordStart = true;

        for (var index = 0; index < projectName.Length; index++)
        {
            var character = projectName[index];

            if (!char.IsLetterOrDigit(character))
            {
                isWordStart = true;
                continue;
            }

            // A capital following a lower case letter starts a word within a run like "IntegrationTest".
            if (index > 0 &&
                char.IsUpper(character) &&
                char.IsLower(projectName[index - 1]))
            {
                isWordStart = true;
            }

            if (!isWordStart)
            {
                continue;
            }

            initials.Append(char.ToUpperInvariant(character));
            isWordStart = false;

            if (initials.Length == MaxInitials)
            {
                break;
            }
        }

        if (initials.Length == 0)
        {
            return UnknownInitials;
        }

        return initials.ToString();
    }

    /// <summary>
    /// Returns the fill colour of the project's avatar tile as a hex string.
    /// </summary>
    public static string GetTileColorHex(string projectName)
    {
        var name = projectName ?? string.Empty;

        // The colour comes from the whole name rather than the initials, because projects that share
        // initials ("Test Examples", "TestEmpty") need the colour to tell them apart.
        var index = (int)(HashName(name) % (uint)TileColors.Length);

        return TileColors[index];
    }

    // FNV-1a. String.GetHashCode is seeded per process, which would repaint every avatar on each launch.
    private static uint HashName(string name)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var character in name)
        {
            hash ^= char.ToLowerInvariant(character);
            hash *= prime;
        }

        return hash;
    }
}
