namespace Celbridge.FileSystem;

/// <summary>
/// Marks a type, member, or assembly as exempt from the convention that bans
/// direct use of System.IO static file and directory facades outside
/// Celbridge.FileSystem. Applied at the documented carve-outs (pre-DI
/// bootstrap, embedded-resource readers). The DirectFileSystemAccessAnalyzer
/// (CEL_FS_001) enforces the convention and honours this marker. Code inside
/// Celbridge.FileSystem itself is exempt by assembly and does not need it.
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
