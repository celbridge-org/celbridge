namespace Celbridge.Packages;

/// <summary>
/// Holds credentials for the package registry API.
/// The default values are empty strings. To provide real credentials, create a
/// PackageApiCredentials.private.cs file (gitignored) with a static constructor
/// that sets these fields.
/// </summary>
internal static partial class PackageApiCredentials
{
    internal static string BaseUrl = "";
    internal static string Username = "";
    internal static string Password = "";
}
