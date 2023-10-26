using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Celbridge.Utils
{
    public static class AssemblyUtils
    {
        public static async Task<Result<string>> CompileAssembly(string sourceFolder, string assemblyName, List<string> assemblies) 
        { 
            DirectoryInfo d = new DirectoryInfo(sourceFolder);
            string[] sourceFiles = d.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                .Select(a => a.FullName)
                .ToArray();

            Result<string> DoCompile()
            {
                try
                {
                    var syntaxTrees = new List<SyntaxTree>();
                    foreach (string file in sourceFiles)
                    {
                        string code = File.ReadAllText(file);
                        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
                        syntaxTrees.Add(tree);
                    }

                    // Build the list of assembly locations to reference in the new assembly
                    List<string> assemblyLocations = new ()
                    {
                        // System.Runtime is needed if you inherit from a class instead of an interface - dunno why
                        Assembly.Load("System.Runtime").Location,
                        typeof(object).Assembly.Location,
                        typeof(SyntaxTree).Assembly.Location,
                        typeof(CSharpSyntaxTree).Assembly.Location,
                        typeof(CelRuntime.Environment).Assembly.Location,
                    };
                    assemblyLocations.AddRange(assemblies);

                    // Convert each assembly location to a meta data reference
                    List<MetadataReference> references = new();
                    foreach (var assemblyLocation in assemblyLocations) 
                    {
                        var metadataReference = MetadataReference.CreateFromFile(assemblyLocation);
                        references.Add(metadataReference);
                    }

                    var compilation = CSharpCompilation.Create($"{assemblyName}.dll",
                        syntaxTrees,
                        references,
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                    var assemblyFile = Path.Combine(sourceFolder, $"{assemblyName}.dll");

                    var result = compilation.Emit(assemblyFile);
                    if (!result.Success)
                    {
                        File.Delete(assemblyFile);

                        // Just return the first compile error for now
                        var message = result.Diagnostics[0].ToString();
                        return new ErrorResult<string>(message);
                    }

                    return new SuccessResult<string>(assemblyFile);
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>(ex.Message);
                }
            }

            // This may take a few seconds so run it in the background to allow the UI to update.
            var compileResult = await Task.Run(DoCompile);
            if (compileResult is ErrorResult<string> compileError)
            {
                return new ErrorResult<string>($"Failed to compile assembly. {compileError.Message}");
            }
            var assemblyFile = compileResult.Data!;

            return new SuccessResult<string>(assemblyFile);
        }
    }
}
