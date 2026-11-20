using System.Runtime.InteropServices;
using System.Xml.Linq;
using Celbridge.Dependencies;

namespace Celbridge.LanguageServers.Helpers;

/// <summary>
/// One platform's TypeScript package: the platform, the npm package's name and the registry's integrity value for
/// the pinned version.
/// </summary>
public sealed record TypeScriptPackage(string Platform, string Name, string Integrity);

/// <summary>
/// The TypeScript release pinned by this build of Celbridge, with an npm package for each platform. The assembly
/// embeds the typescript_pin.xml file.
/// </summary>
public sealed record TypeScriptPinnedRelease(string Version, string RegistryUrl, IReadOnlyList<TypeScriptPackage> Packages)
{
    private const string ResourceName = "typescript_pin.xml";
    private const string Sha512Prefix = "sha512-";

    /// <summary>
    /// Reads the assembly's embedded pin.
    /// </summary>
    public static TypeScriptPinnedRelease Load()
    {
        var assembly = typeof(TypeScriptPinnedRelease).Assembly;
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
    /// Reads a pin from the text of a typescript_pin.xml file.
    /// </summary>
    public static TypeScriptPinnedRelease Parse(string pinText)
    {
        var document = XDocument.Parse(pinText);
        var root = document.Root;
        if (root is null)
        {
            throw new FormatException("The TypeScript pin has no root element");
        }

        var version = ReadRequiredAttribute(root, "Version");
        var registryUrl = ReadRequiredAttribute(root, "RegistryUrl").TrimEnd('/');

        var packages = new List<TypeScriptPackage>();
        foreach (var element in root.Elements("Package"))
        {
            var package = new TypeScriptPackage(
                ReadRequiredAttribute(element, "Platform"),
                ReadRequiredAttribute(element, "Name"),
                ReadRequiredAttribute(element, "Integrity"));

            // A malformed hash fails here, when the pin is read.
            ConvertIntegrity(package.Integrity);

            packages.Add(package);
        }

        return new TypeScriptPinnedRelease(version, registryUrl, packages);
    }

    /// <summary>
    /// Returns the package for the platform, or null when the pin lists none for the platform.
    /// </summary>
    public TypeScriptPackage? FindPackage(string platform)
    {
        return Packages.FirstOrDefault(package => package.Platform == platform);
    }

    /// <summary>
    /// Returns the address of the package's archive in the npm registry.
    /// </summary>
    public string GetArchiveUrl(TypeScriptPackage package)
    {
        // The archive's file name leaves out the package's scope.
        var unscopedName = package.Name;
        var separatorIndex = unscopedName.LastIndexOf('/');
        if (separatorIndex >= 0)
        {
            unscopedName = unscopedName.Substring(separatorIndex + 1);
        }

        return $"{RegistryUrl}/{package.Name}/-/{unscopedName}-{Version}.tgz";
    }

    /// <summary>
    /// Returns the archive's expected hash, converted from the package's integrity value.
    /// </summary>
    public static DependencyHash GetArchiveHash(TypeScriptPackage package)
    {
        return ConvertIntegrity(package.Integrity);
    }

    /// <summary>
    /// Returns the npm platform name for the running operating system and processor. Returns null for a platform
    /// outside the pin's list of packages.
    /// </summary>
    public static string? GetRunningPlatform()
    {
        var architecture = RuntimeInformation.OSArchitecture;

        string? processor = architecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => null
        };
        if (processor is null)
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"darwin-{processor}";
        }

        if (OperatingSystem.IsWindows())
        {
            return $"win32-{processor}";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"linux-{processor}";
        }

        return null;
    }

    // The registry writes a hash as the algorithm's name, a hyphen and the hash in base64.
    private static DependencyHash ConvertIntegrity(string integrity)
    {
        if (!integrity.StartsWith(Sha512Prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"The TypeScript pin's Integrity value '{integrity}' is not a SHA-512 hash");
        }

        var hashBytes = Convert.FromBase64String(integrity.Substring(Sha512Prefix.Length));
        if (hashBytes.Length != 64)
        {
            throw new FormatException($"The TypeScript pin's Integrity value '{integrity}' is not 64 bytes long");
        }

        var hashText = Convert.ToHexStringLower(hashBytes);

        return new DependencyHash(DependencyHashAlgorithm.Sha512, hashText);
    }

    private static string ReadRequiredAttribute(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"The TypeScript pin's '{element.Name}' element has no '{attributeName}' attribute");
        }

        return value;
    }
}
