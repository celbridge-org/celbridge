using System.Text;

namespace Celbridge.Utilities;

/// <summary>
/// Splits a command line into arguments by the rules Windows uses.
/// </summary>
public static class WindowsCommandLine
{
    /// <summary>
    /// Splits an argument string into arguments. Whitespace separates arguments, double quotes group text that
    /// contains whitespace, and a backslash escapes a double quote.
    /// </summary>
    public static IReadOnlyList<string> Split(string argumentText)
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
