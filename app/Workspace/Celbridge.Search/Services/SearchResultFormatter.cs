using System.Text;

namespace Celbridge.Search.Services;

/// <summary>
/// Formats search result display text with context around matches.
/// </summary>
public class SearchResultFormatter
{
    private const int MaxPrefixChars = 25;
    private const int MaxDisplayLength = 100;

    /// <summary>
    /// Creates a context line for display, ensuring the match appears within the first ~30 characters.
    /// Returns the display text and the adjusted match start position within that text.
    /// </summary>
    public (string DisplayText, int MatchStart) FormatContextLine(string line, int matchStart, int matchLength)
    {
        // Trim leading whitespace and track the offset
        int leadingWhitespace = 0;
        while (leadingWhitespace < line.Length && char.IsWhiteSpace(line[leadingWhitespace]))
        {
            leadingWhitespace++;
        }
        
        var trimmedLine = line.Substring(leadingWhitespace).TrimEnd();
        
        // Adjust match position for trimmed leading whitespace
        int adjustedMatchStart = matchStart - leadingWhitespace;
        
        // Clamp to valid range
        adjustedMatchStart = Math.Clamp(adjustedMatchStart, 0, Math.Max(0, trimmedLine.Length - 1));
        
        // If the trimmed line is short AND the match is near the start, return the whole line
        if (trimmedLine.Length <= MaxDisplayLength && adjustedMatchStart <= MaxPrefixChars)
        {
            return (trimmedLine, adjustedMatchStart);
        }

        // Calculate context start: ensure match is within first MaxPrefixChars characters of the displayed text
        int contextStart = 0;
        bool needsPrefix = false;
        
        if (adjustedMatchStart > MaxPrefixChars)
        {
            // Need to skip some text to bring the match closer to the start
            contextStart = adjustedMatchStart - MaxPrefixChars;
            
            // Look for a word boundary to break at
            int wordBoundary = FindWordBoundaryForward(trimmedLine, contextStart);
            if (wordBoundary > 0 && wordBoundary <= adjustedMatchStart)
            {
                contextStart = wordBoundary;
            }
            
            needsPrefix = true;
        }
        
        // Calculate available length for content
        int availableLength = MaxDisplayLength;
        if (needsPrefix)
        {
            availableLength -= 3; // Reserve space for "..."
        }
        
        // Calculate context end
        int contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
        bool needsSuffix = contextEnd < trimmedLine.Length;
        
        if (needsSuffix)
        {
            availableLength -= 3; // Reserve space for "..."
            contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
            
            // Try to break at a word boundary, but ensure we don't cut off the match
            int matchEndInTrimmed = adjustedMatchStart + matchLength;
            int wordBoundary = FindWordBoundaryBackward(trimmedLine, contextEnd);
            if (wordBoundary > matchEndInTrimmed && wordBoundary > contextStart)
            {
                contextEnd = wordBoundary;
            }
        }
        
        // Ensure valid bounds
        contextStart = Math.Max(0, contextStart);
        contextEnd = Math.Min(trimmedLine.Length, contextEnd);
        
        if (contextEnd <= contextStart)
        {
            // Fallback: just show from match position
            contextStart = adjustedMatchStart;
            contextEnd = Math.Min(trimmedLine.Length, contextStart + availableLength);
            needsPrefix = contextStart > 0;
            needsSuffix = contextEnd < trimmedLine.Length;
        }
        
        // Build the result
        var result = new StringBuilder();
        
        if (needsPrefix)
        {
            result.Append("...");
        }
        
        result.Append(trimmedLine.AsSpan(contextStart, contextEnd - contextStart));
        
        if (needsSuffix)
        {
            result.Append("...");
        }
        
        // Calculate the match position in the display text
        int displayMatchStart = adjustedMatchStart - contextStart;
        if (needsPrefix)
        {
            displayMatchStart += 3; // Account for "..." prefix
        }
        
        // Clamp to valid range within display text
        displayMatchStart = Math.Clamp(displayMatchStart, 0, Math.Max(0, result.Length - 1));
        
        return (result.ToString(), displayMatchStart);
    }

    /// <summary>
    /// Finds the position of the nearest word boundary at or after the given position.
    /// Uses Unicode-aware character classification.
    /// Returns the position after the boundary character, or -1 if not found.
    /// </summary>
    private static int FindWordBoundaryForward(string text, int position)
    {
        if (position < 0 || position >= text.Length)
        {
            return -1;
        }
        
        // Look for whitespace or punctuation that indicates a word boundary
        for (int i = position; i < text.Length && i < position + 20; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSeparator(c))
            {
                // Skip consecutive whitespace/punctuation and return position of next content
                int j = i + 1;
                while (j < text.Length && (char.IsWhiteSpace(text[j]) || char.IsPunctuation(text[j]) || char.IsSeparator(text[j])))
                {
                    j++;
                }
                return j < text.Length ? j : -1;
            }
        }
        
        return -1;
    }

    /// <summary>
    /// Finds the position of the nearest word boundary at or before the given position.
    /// Uses Unicode-aware character classification.
    /// Returns -1 if no suitable boundary is found.
    /// </summary>
    private static int FindWordBoundaryBackward(string text, int position)
    {
        if (position <= 0 || position > text.Length)
        {
            return -1;
        }
        
        // Look for whitespace or punctuation that indicates a word boundary
        for (int i = position - 1; i >= 0 && i >= position - 20; i--)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSeparator(c))
            {
                // Return position after the boundary character
                return i + 1;
            }
        }
        
        return -1;
    }
}
