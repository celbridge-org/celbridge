using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Celbridge.Python.Helpers;

/// <summary>
/// One platform's uv release asset: the platform's target triple, the asset's file name and its SHA-256 hash. Folder
/// names the folder inside the archive that contains the binaries, and is empty when the binaries are at the root.
/// </summary>
public sealed record UvArchive(string Platform, string Name, string Sha256, string Folder);

/// <summary>
/// The uv release that this build of Celbridge pins, with an archive for each platform. The assembly embeds the
/// uv_pin.xml file, and the build reads the same file.
/// </summary>
public sealed record UvPin(string Version, string ReleasesUrl, IReadOnlyList<UvArchive> Archives)
{
    private const string ResourceName = "uv_pin.xml";

    /// <summary>
    /// Reads the assembly's embedded pin.
    /// </summary>
    public static UvPin Load()
    {
        var assembly = typeof(UvPin).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"The assembly '{assembly.GetName().Name}' embeds no '{ResourceName}'");
        }

        using var reader = new StreamReader(stream);
        var pinText = reader.ReadToEnd();

        return Parse(pinText);
    }

    /// <summary>
    /// Reads a pin from the text of a uv_pin.xml file.
    /// </summary>
    public static UvPin Parse(string pinText)
    {
        var document = XDocument.Parse(pinText);
        var root = document.Root;
        if (root is null)
        {
            throw new FormatException("The uv pin has no root element");
        }

        var version = ReadRequiredAttribute(root, "Version");
        var releasesUrl = ReadRequiredAttribute(root, "ReleasesUrl").TrimEnd('/');

        var archives = new List<UvArchive>();
        foreach (var element in root.Elements("Archive"))
        {
            var archive = new UvArchive(
                ReadRequiredAttribute(element, "Platform"),
                ReadRequiredAttribute(element, "Name"),
                ReadRequiredAttribute(element, "Sha256"),
                element.Attribute("Folder")?.Value ?? string.Empty);

            archives.Add(archive);
        }

        return new UvPin(version, releasesUrl, archives);
    }

    /// <summary>
    /// Returns the archive for the platform's target triple, or null when uv publishes none for the platform.
    /// </summary>
    public UvArchive? FindArchive(string platform)
    {
        return Archives.FirstOrDefault(archive => archive.Platform == platform);
    }

    /// <summary>
    /// Returns the address of the archive in uv's release.
    /// </summary>
    public string GetArchiveUrl(UvArchive archive)
    {
        return $"{ReleasesUrl}/{Version}/{archive.Name}";
    }

    /// <summary>
    /// Returns uv's target triple for the running operating system and processor. Returns null for a platform
    /// outside the pin's list of archives.
    /// </summary>
    public static string? GetRunningPlatform()
    {
        var architecture = RuntimeInformation.OSArchitecture;

        string? processor = architecture switch
        {
            Architecture.Arm64 => "aarch64",
            Architecture.X64 => "x86_64",
            _ => null
        };
        if (processor is null)
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"{processor}-apple-darwin";
        }

        if (OperatingSystem.IsWindows())
        {
            return $"{processor}-pc-windows-msvc";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"{processor}-unknown-linux-gnu";
        }

        return null;
    }

    private static string ReadRequiredAttribute(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"The uv pin's '{element.Name}' element has no '{attributeName}' attribute");
        }

        return value;
    }
}
