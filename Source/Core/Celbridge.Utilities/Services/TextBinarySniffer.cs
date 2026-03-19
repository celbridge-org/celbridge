using System.Buffers;
using System.Text;

namespace Celbridge.Utilities;

/// <summary>
/// Provides heuristic detection of whether a file or stream contains text or binary data.
/// Handles UTF-8, UTF-16, UTF-32, and legacy 8-bit text encodings.
/// </summary>
public static class TextBinarySniffer
{
    private const int SampleSize = 8192;

    /// <summary>
    /// Known binary file extensions for fast-path detection.
    /// </summary>
    private static readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Executables and libraries
        ".exe", ".dll", ".pdb", ".obj", ".o", ".a", ".lib",
        ".so", ".dylib", ".bin", ".dat",
        // Archives
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        // Audio
        ".mp3", ".wav", ".ogg", ".flac", ".aac",
        // Video
        ".mp4", ".avi", ".mkv", ".mov", ".webm",
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // Fonts
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        // Compiled code
        ".pyc", ".pyo", ".class",
        // Databases
        ".db", ".sqlite", ".sqlite3",
        // Packages
        ".nupkg", ".snupkg", ".vsix", ".msi", ".cab"
    };

    /// <summary>
    /// Quickly checks if a file extension indicates a binary file format.
    /// This is a fast path that avoids reading file content.
    /// </summary>
    public static bool IsBinaryExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        // Normalize: ensure it starts with a dot
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return _binaryExtensions.Contains(extension);
    }

    /// <summary>
    /// Determines if a file is likely a text file by examining its content.
    /// </summary>
    public static Result<bool> IsTextFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Result<bool>.Fail("File path is null or empty");
        }

        if (!File.Exists(path))
        {
            return Result<bool>.Fail($"File does not exist: {path}");
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Result<bool>.Ok(IsTextStream(stream));
        }
        catch (Exception ex)
        {
            return Result<bool>.Fail($"Failed to read file: {path}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Determines if the provided content appears to be text (not binary).
    /// </summary>
    public static bool IsTextContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return true; // Empty content is considered text
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        return IsTextBytes(bytes);
    }

    /// <summary>
    /// Determines if a stream contains text data by examining its content.
    /// </summary>
    private static bool IsTextStream(Stream stream)
    {
        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(SampleSize);
        try
        {
            int read = stream.Read(rented, 0, SampleSize);
            if (read == 0)
            {
                return true; // Empty file, treat as text
            }

            var bytes = new ReadOnlySpan<byte>(rented, 0, read);
            return IsTextBytes(bytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Determines if the provided bytes appear to be text data.
    /// </summary>
    private static bool IsTextBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return true; // Empty content, treat as text
        }

        // 1. BOM => text
        if (HasTextBom(bytes))
        {
            return true;
        }

        // 2. Check for NUL bytes - could be UTF-16/UTF-32 without BOM or actual binary
        if (bytes.IndexOf((byte)0) >= 0)
        {
            // Try to detect UTF-16/UTF-32 without BOM before rejecting as binary
            if (IsValidUtf16(bytes) || IsValidUtf32(bytes))
            {
                return true; // Valid UTF-16/32 text encoding
            }
            
            return false; // Actual binary with NUL bytes
        }

        // 3. Strict UTF-8 decode test
        if (IsValidUtf8(bytes))
        {
            // If it's valid UTF-8, still guard against "mostly control chars"
            return LooksLikeMostlyText(bytes);
        }

        // 4. If it's not valid UTF-8, it might still be legacy 8-bit text.
        // Decide using a control/printable heuristic.
        return LooksLikeMostlyText(bytes);
    }

    /// <summary>
    /// Checks if the buffer starts with a Unicode Byte Order Mark (BOM).
    /// </summary>
    private static bool HasTextBom(ReadOnlySpan<byte> b) =>
        b.StartsWith([(byte)0xEF, (byte)0xBB, (byte)0xBF]) ||               // UTF-8
        b.StartsWith([(byte)0xFF, (byte)0xFE, (byte)0x00, (byte)0x00]) ||   // UTF-32 LE (check before UTF-16 LE)
        b.StartsWith([(byte)0x00, (byte)0x00, (byte)0xFE, (byte)0xFF]) ||   // UTF-32 BE
        b.StartsWith([(byte)0xFF, (byte)0xFE]) ||                           // UTF-16 LE
        b.StartsWith([(byte)0xFE, (byte)0xFF]);                             // UTF-16 BE

    /// <summary>
    /// Validates whether the bytes represent valid UTF-8 encoded text.
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        // Strict mode: throw on invalid sequences
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            utf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the bytes appear to be valid UTF-16 (LE or BE) without BOM.
    /// Uses heuristics: tries to decode and checks if result is valid text.
    /// Also checks for UTF-16 structural patterns to avoid false positives.
    /// </summary>
    private static bool IsValidUtf16(ReadOnlySpan<byte> bytes)
    {
        // Need at least 2 bytes for UTF-16
        if (bytes.Length < 2)
        {
            return false;
        }

        // UTF-16 requires even number of bytes
        if (bytes.Length % 2 != 0)
        {
            return false;
        }

        // Check for UTF-16 LE patterns first (most common)
        if (LooksLikeUtf16LE(bytes) && TryDecodeUtf16(bytes, Encoding.Unicode))
        {
            return true;
        }

        // Check for UTF-16 BE patterns
        if (LooksLikeUtf16BE(bytes) && TryDecodeUtf16(bytes, Encoding.BigEndianUnicode))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if bytes match UTF-16 LE patterns (every other byte is often 0x00 for ASCII-range text).
    /// </summary>
    private static bool LooksLikeUtf16LE(ReadOnlySpan<byte> bytes)
    {
        // For UTF-16 LE, ASCII characters have pattern: [char, 0x00]
        // Count how many even-positioned bytes are printable ASCII and odd-positioned are 0x00
        int asciiLikeCount = 0;
        int totalPairs = bytes.Length / 2;

        for (int i = 0; i < bytes.Length - 1; i += 2)
        {
            byte lowByte = bytes[i];
            byte highByte = bytes[i + 1];

            // Check if this looks like an ASCII character in UTF-16 LE
            if (highByte == 0x00 && (lowByte >= 0x20 && lowByte <= 0x7E || lowByte == 0x09 || lowByte == 0x0A || lowByte == 0x0D))
            {
                asciiLikeCount++;
            }
        }

        // If at least 50% of pairs look like ASCII in UTF-16 LE, it's likely UTF-16 LE
        return asciiLikeCount >= totalPairs * 0.5;
    }

    /// <summary>
    /// Checks if bytes match UTF-16 BE patterns.
    /// </summary>
    private static bool LooksLikeUtf16BE(ReadOnlySpan<byte> bytes)
    {
        // For UTF-16 BE, ASCII characters have pattern: [0x00, char]
        int asciiLikeCount = 0;
        int totalPairs = bytes.Length / 2;

        for (int i = 0; i < bytes.Length - 1; i += 2)
        {
            byte highByte = bytes[i];
            byte lowByte = bytes[i + 1];

            // Check if this looks like an ASCII character in UTF-16 BE
            if (highByte == 0x00 && (lowByte >= 0x20 && lowByte <= 0x7E || lowByte == 0x09 || lowByte == 0x0A || lowByte == 0x0D))
            {
                asciiLikeCount++;
            }
        }

        // If at least 50% of pairs look like ASCII in UTF-16 BE, it's likely UTF-16 BE
        return asciiLikeCount >= totalPairs * 0.5;
    }

    /// <summary>
    /// Checks if the bytes appear to be valid UTF-32 without BOM.
    /// </summary>
    private static bool IsValidUtf32(ReadOnlySpan<byte> bytes)
    {
        // Need at least 4 bytes for UTF-32
        if (bytes.Length < 4)
        {
            return false;
        }

        try
        {
            var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true);
            var chars = encoding.GetChars(bytes.ToArray());
            
            // Validate that decoded text looks reasonable (not mostly control characters)
            return IsDecodedTextValid(chars);
        }
        catch
        {
            // Try big-endian UTF-32
            try
            {
                var encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true);
                var chars = encoding.GetChars(bytes.ToArray());
                return IsDecodedTextValid(chars);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Attempts to decode bytes as UTF-16 and validates the result.
    /// </summary>
    private static bool TryDecodeUtf16(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        try
        {
            var decoder = encoding.GetDecoder();
            decoder.Fallback = DecoderFallback.ExceptionFallback;
            
            var chars = encoding.GetChars(bytes.ToArray());
            
            // Validate that decoded text looks reasonable
            return IsDecodedTextValid(chars);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that decoded text contains reasonable characters (not binary garbage).
    /// Checks for valid character patterns and absence of excessive control characters.
    /// </summary>
    private static bool IsDecodedTextValid(char[] chars)
    {
        if (chars.Length == 0)
        {
            return true;
        }

        int suspicious = 0;
        int printable = 0;

        foreach (char c in chars)
        {
            // Allow common whitespace and control characters
            if (c == '\t' || c == '\n' || c == '\r' || c == '\f')
            {
                continue;
            }

            // Check for printable characters (basic ASCII and Unicode)
            if (c >= 0x20 && c < 0x7F) // ASCII printable
            {
                printable++;
            }
            else if (c >= 0x80 && c < 0xFFFE) // Unicode range (excluding special markers)
            {
                // Most Unicode characters are valid for text
                // Exclude replacement characters and other special markers
                if (c == 0xFFFD || c == 0xFFFE || c == 0xFFFF)
                {
                    suspicious++;
                }
                else
                {
                    printable++;
                }
            }
            else if (c < 0x20) // Control characters (excluding allowed ones above)
            {
                suspicious++;
            }
        }

        // If we have very few printable characters, it's likely binary
        if (chars.Length > 10 && printable < chars.Length * 0.3)
        {
            return false;
        }

        // Check suspicious character ratio (similar to the 2% threshold for bytes)
        double ratio = (double)suspicious / chars.Length;
        return ratio <= 0.05; // Slightly more lenient for decoded text
    }

    /// <summary>
    /// Checks if the bytes appear to be mostly printable text characters.
    /// Allows common control characters (tab, LF, CR, FF, ESC) and high bytes (for UTF-8/legacy encodings).
    /// </summary>
    private static bool LooksLikeMostlyText(ReadOnlySpan<byte> bytes)
    {
        int suspicious = 0;

        foreach (byte b in bytes)
        {
            // Allow common control characters: tab, LF, CR, form-feed, ESC (for ANSI logs)
            if (b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0C || b == 0x1B)
            {
                continue;
            }

            // ASCII printable range
            if (b >= 0x20 && b <= 0x7E)
            {
                continue;
            }

            // High bytes (>= 0x80) could be UTF-8 multibyte or legacy 8-bit text
            // Don't count them as suspicious
            if (b >= 0x80)
            {
                continue;
            }

            suspicious++;
        }

        // Threshold: if more than 2% suspicious control characters, it's probably binary
        double ratio = (double)suspicious / bytes.Length;
        return ratio <= 0.02;
    }
}
