using System.Text;

namespace Celbridge.Projects;

/// <summary>
/// Finds the project file named on the application's command line.
/// </summary>
public static class ProjectCommandLine
{
    /// <summary>
    /// Returns true if the path has the project file extension, in any case.
    /// </summary>
    public static bool IsProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ProjectConstants.ProjectFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the full path of the first argument that has the project file extension, or null if there is
    /// none. A relative path is resolved against the working folder.
    /// </summary>
    public static string? FindProjectFile(IEnumerable<string> arguments, string workingFolderPath)
    {
        var projectFileArgument = arguments.FirstOrDefault(IsProjectFile);
        if (string.IsNullOrEmpty(projectFileArgument))
        {
            return null;
        }

        return Path.GetFullPath(projectFileArgument, workingFolderPath);
    }

    /// <summary>
    /// Splits an argument string into arguments by the rules Windows uses to split a command line. Whitespace
    /// separates arguments, double quotes group text that contains whitespace, and a backslash escapes a
    /// double quote.
    /// </summary>
    public static IReadOnlyList<string> SplitWindowsArguments(string argumentText)
    {
        var arguments = new List<string>();
        var argument = new StringBuilder();
        var inArgument = false;
        var inQuotes = false;

        var index = 0;
        while (index < argumentText.Length)
        {
            var character = argumentText[index];

            if (character == '\\')
            {
                var backslashCount = 0;
                while (index < argumentText.Length
                    && argumentText[index] == '\\')
                {
                    backslashCount++;
                    index++;
                }

                inArgument = true;

                var beforeQuote = index < argumentText.Length
                    && argumentText[index] == '"';
                if (!beforeQuote)
                {
                    argument.Append('\\', backslashCount);
                    continue;
                }

                // Each pair of backslashes before a quote gives one backslash. An odd one out escapes the quote.
                argument.Append('\\', backslashCount / 2);
                if (backslashCount % 2 == 1)
                {
                    argument.Append('"');
                    index++;
                }
                continue;
            }

            if (character == '"')
            {
                inArgument = true;

                // Two quotes inside a quoted part give one literal quote.
                var isDoubledQuote = inQuotes
                    && index + 1 < argumentText.Length
                    && argumentText[index + 1] == '"';
                if (isDoubledQuote)
                {
                    argument.Append('"');
                    index += 2;
                    continue;
                }

                inQuotes = !inQuotes;
                index++;
                continue;
            }

            var isSeparator = character == ' '
                || character == '\t';
            if (isSeparator
                && !inQuotes)
            {
                if (inArgument)
                {
                    arguments.Add(argument.ToString());
                    argument.Clear();
                    inArgument = false;
                }
                index++;
                continue;
            }

            argument.Append(character);
            inArgument = true;
            index++;
        }

        if (inArgument)
        {
            arguments.Add(argument.ToString());
        }

        return arguments;
    }
}
