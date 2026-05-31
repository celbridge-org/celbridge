namespace Celbridge.FileSystem;

/// <summary>
/// Marks a type, member, or assembly as exempt from the gateway rule that
/// bans direct use of System.IO static file and directory facades outside
/// Celbridge.FileSystem. Applied at the documented carve-outs (pre-DI
/// bootstrap, embedded-resource readers) and recognised by the Roslyn
/// analyzer landed in Phase 5.
/// </summary>
[AttributeUsage(
    AttributeTargets.Assembly
        | AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Method
        | AttributeTargets.Property
        | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AllowDirectFileSystemAccessAttribute : Attribute
{
}
