using System.Collections.Immutable;
using Celbridge.FileSystem.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Celbridge.Tests.FileSystem.Analyzers;

/// <summary>
/// Tests for DirectFileSystemAccessAnalyzer (CEL_FS_001) — the build-time guard
/// that bans direct System.IO file and directory facades outside the gateway.
/// Snippets are compiled in-memory and the analyzer is run against them.
/// </summary>
[TestFixture]
public class DirectFileSystemAccessAnalyzerTests
{
    private const string ProductAssemblyName = "Celbridge.SampleProduct";

    [Test]
    public async Task QualifiedFileWrite_IsFlagged()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace SampleProduct;

            public static class Sample
            {
                public static async Task Run(string path, byte[] bytes)
                {
                    await System.IO.File.WriteAllBytesAsync(path, bytes);
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be(DirectFileSystemAccessAnalyzer.DiagnosticId);
        diagnostics[0].GetMessage().Should().Contain("System.IO.File");
    }

    [Test]
    public async Task UsingImportedFileWrite_IsFlagged()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;

            namespace SampleProduct;

            public static class Sample
            {
                public static async Task Run(string path, byte[] bytes)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
    }

    [Test]
    public async Task DirectoryExists_IsFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static bool Run(string path) => Directory.Exists(path);
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
    }

    [Test]
    public async Task NewFileInfo_IsFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static long Run(string path)
                {
                    var info = new FileInfo(path);
                    return info.Length;
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
    }

    [Test]
    public async Task FileSystemWatcherType_IsFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public class Sample
            {
                public static void Run(FileSystemWatcher watcher)
                {
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle();
    }

    [Test]
    public async Task PathCombine_IsNotFlagged()
    {
        // Path is deliberately excluded: it does pure path-string manipulation
        // with no filesystem access, and the gateway offers no replacement.
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static string Run(string a, string b) => Path.Combine(a, b);
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task StreamHandling_IsNotFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static void Run(Stream stream)
                {
                    try
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task NamespaceImportWithoutUsage_IsNotFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static int Run() => 42;
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task InsideGatewayAssembly_IsNotFlagged()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;

            namespace Celbridge.FileSystem.Services;

            public static class Sample
            {
                public static async Task Run(string path, byte[] bytes)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source, assemblyName: "Celbridge.FileSystem");

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task InsideTestAssembly_IsNotFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                public static bool Run(string path) => Directory.Exists(path);
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source, assemblyName: "Celbridge.SomeModule.Tests");

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task AllowAttributeOnMethod_IsNotFlagged()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;
            using Celbridge.FileSystem;

            namespace SampleProduct;

            public static class Sample
            {
                [AllowDirectFileSystemAccess]
                public static async Task Run(string path, byte[] bytes)
                {
                    await File.WriteAllBytesAsync(path, bytes);
                }
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task AllowAttributeOnClass_IsNotFlagged()
    {
        const string source = """
            using System.IO;
            using Celbridge.FileSystem;

            namespace SampleProduct;

            [AllowDirectFileSystemAccess]
            public static class Sample
            {
                public static bool Run(string path) => Directory.Exists(path);
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task AllowAttributeOnAssembly_IsNotFlagged()
    {
        const string source = """
            using System.IO;
            using Celbridge.FileSystem;

            [assembly: AllowDirectFileSystemAccess]

            namespace SampleProduct;

            public static class Sample
            {
                public static bool Run(string path) => Directory.Exists(path);
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    [Test]
    public async Task CrefDocumentationReference_IsNotFlagged()
    {
        const string source = """
            using System.IO;

            namespace SampleProduct;

            public static class Sample
            {
                /// <summary>
                /// Mirrors <see cref="System.IO.File"/> semantics.
                /// </summary>
                public static int Run() => 0;
            }
            """;

        var diagnostics = await GetGatewayDiagnosticsAsync(source);

        diagnostics.Should().BeEmpty();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetGatewayDiagnosticsAsync(
        string source,
        string assemblyName = ProductAssemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            FrameworkReferences.Value,
            compilationOptions);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DirectFileSystemAccessAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        var analyzerCrashes = allDiagnostics.Where(diagnostic => diagnostic.Id == "AD0001").ToArray();
        if (analyzerCrashes.Length > 0)
        {
            throw new InvalidOperationException($"Analyzer threw an exception: {analyzerCrashes[0].GetMessage()}");
        }

        return allDiagnostics
            .Where(diagnostic => diagnostic.Id == DirectFileSystemAccessAnalyzer.DiagnosticId)
            .ToImmutableArray();
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkReferences = new(LoadFrameworkReferences);

    private static ImmutableArray<MetadataReference> LoadFrameworkReferences()
    {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        var referencedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var assemblyPath in trustedAssemblies.Split(Path.PathSeparator))
        {
            if (assemblyPath.Length == 0)
            {
                continue;
            }

            if (referencedFileNames.Add(Path.GetFileName(assemblyPath)))
            {
                references.Add(MetadataReference.CreateFromFile(assemblyPath));
            }
        }

        // Ensure the gateway assembly (which carries AllowDirectFileSystemAccessAttribute)
        // is referenced even if it is not on the trusted-platform list.
        var gatewayAssemblyPath = typeof(AllowDirectFileSystemAccessAttribute).Assembly.Location;
        if (referencedFileNames.Add(Path.GetFileName(gatewayAssemblyPath)))
        {
            references.Add(MetadataReference.CreateFromFile(gatewayAssemblyPath));
        }

        return references.ToImmutable();
    }
}
