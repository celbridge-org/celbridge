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

        // 2. NUL bytes without BOM => binary
        // (UTF-16/UTF-32 without BOM is rare; treating as binary is the pragmatic choice)
        if (bytes.IndexOf((byte)0) >= 0)
        {
            return false;
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
