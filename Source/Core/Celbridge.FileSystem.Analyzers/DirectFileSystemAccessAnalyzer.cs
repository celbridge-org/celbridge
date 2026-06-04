using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Celbridge.FileSystem.Analyzers;

/// <summary>
/// Reports direct use of the System.IO static file and directory facades
/// outside the Celbridge.FileSystem gateway. Filesystem access must flow
/// through ILocalFileSystem so it stays substitutable, testable, and portable
/// to non-local substrates.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectFileSystemAccessAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CEL_FS_001";

    private const string GatewayAssemblyName = "Celbridge.FileSystem";
    private const string AllowAttributeName = "AllowDirectFileSystemAccessAttribute";
    private const string AllowAttributeNamespace = "Celbridge.FileSystem";
    private const string SystemIONamespace = "System.IO";

    // The IO-performing facades. System.IO.Path is deliberately excluded: it is
    // pure path-string manipulation with no filesystem access, and the gateway
    // offers no replacement for it. Stream, FileStream, IOException, SeekOrigin
    // and similar types are also permitted, since consumers of the gateway's
    // stream methods legitimately handle them.
    private static readonly ImmutableHashSet<string> BannedTypeNames =
        ImmutableHashSet.Create(
            "File",
            "Directory",
            "FileInfo",
            "DirectoryInfo",
            "FileSystemWatcher");

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Direct System.IO filesystem access outside the gateway",
        messageFormat: "'System.IO.{0}' must not be used directly. Route filesystem access through ILocalFileSystem, or mark the carve-out with [AllowDirectFileSystemAccess].",
        category: "Celbridge.FileSystem",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Filesystem reads and writes must pass through the ILocalFileSystem gateway. Documented carve-outs (pre-DI bootstrap, embedded-resource readers, process working directory) are marked with [AllowDirectFileSystemAccess].");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // Assembly-wide carve-outs are computed once and captured: the gateway
        // assembly itself, test assemblies, and an assembly-level opt-out.
        var isExemptAssembly =
            context.Compilation.AssemblyName == GatewayAssemblyName
            || IsTestAssembly(context.Compilation, context.Options)
            || HasAllowAttribute(context.Compilation.Assembly);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeIdentifierName(nodeContext, isExemptAssembly),
            SyntaxKind.IdentifierName);
    }

    private static void AnalyzeIdentifierName(SyntaxNodeAnalysisContext context, bool isExemptAssembly)
    {
        if (isExemptAssembly)
        {
            return;
        }

        var identifierName = (IdentifierNameSyntax)context.Node;

        // Cheap textual screen before consulting the semantic model. Every
        // reference to a banned type names it by one of these simple names,
        // whether written bare or fully qualified.
        if (!BannedTypeNames.Contains(identifierName.Identifier.ValueText))
        {
            return;
        }

        // Documentation cref references (e.g. <see cref="System.IO.File"/>) name
        // the type without using it, so they are not a gateway violation.
        if (identifierName.FirstAncestorOrSelf<CrefSyntax>() is not null)
        {
            return;
        }

        // Using directives (imports and aliases such as 'using File = System.IO.File;')
        // only bind names; they perform no IO. An aliased usage still resolves to the
        // real System.IO type at its call site, so it stays caught there.
        if (identifierName.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(identifierName, context.CancellationToken).Symbol;
        if (symbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (!IsBannedSystemIOType(namedType))
        {
            return;
        }

        // Carve-out: a [AllowDirectFileSystemAccess] on the enclosing member or
        // any containing type exempts the usage.
        var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(identifierName.SpanStart, context.CancellationToken);
        if (IsExemptedByAttribute(enclosingSymbol))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(Rule, identifierName.GetLocation(), namedType.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsBannedSystemIOType(INamedTypeSymbol type)
    {
        if (!BannedTypeNames.Contains(type.Name))
        {
            return false;
        }

        var containingNamespace = type.ContainingNamespace;
        if (containingNamespace is null)
        {
            return false;
        }

        return containingNamespace.ToDisplayString() == SystemIONamespace;
    }

    private static bool IsExemptedByAttribute(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (HasAllowAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAllowAttribute(ISymbol symbol)
    {
        var attributes = symbol.GetAttributes();
        foreach (var attribute in attributes)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                continue;
            }

            if (attributeClass.Name == AllowAttributeName
                && attributeClass.ContainingNamespace?.ToDisplayString() == AllowAttributeNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTestAssembly(Compilation compilation, AnalyzerOptions analyzerOptions)
    {
        var assemblyName = compilation.AssemblyName;
        if (assemblyName is not null
            && (assemblyName.EndsWith(".Tests", StringComparison.Ordinal) || assemblyName.Contains(".Tests.")))
        {
            return true;
        }

        var globalOptions = analyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (globalOptions.TryGetValue("build_property.IsTestProject", out var isTestProject)
            && string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
