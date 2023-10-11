using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using Scriban;
using Celbridge.Models.CelMixins;
using Microsoft.CodeAnalysis;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.Text;

namespace Celbridge.Tasks
{
    public class GenerateCelSignaturesTask
    {
        const string CelSignaturesName = "CelSignatures";

        class CelParameterInfo
        {
            public string Name = string.Empty;
            public string Type = string.Empty;
        };

        class CelSignatureInfo
        {
            public string CelScriptName = string.Empty;
            public string CelName = string.Empty;
            public List<CelParameterInfo> Parameters = new();
            public string ReturnType = string.Empty;
            public string Summary = string.Empty;
        }

        class CelScriptSignatureInfo
        {
            public Dictionary<string, List<CelSignatureInfo>> CelScriptSignatures = new ();
        }

        public async Task<Result<string>> Generate(string projectFolder, string libraryPath)
        {
            try
            {
                // Ensure the CelSignatures folder exists
                var celSignaturesFolder = Path.Combine(libraryPath, CelSignaturesName);
                if (Directory.Exists(celSignaturesFolder))
                {
                    Directory.Delete(celSignaturesFolder, true);
                }
                Directory.CreateDirectory(celSignaturesFolder);

                // Parse the CelScript .cel files and retrieve the signature of every Cel
                var parseResult = await ParseCelScripts(projectFolder);
                if (parseResult is ErrorResult<CelScriptSignatureInfo> parseError)
                {
                    return new ErrorResult<string>($"Failed to generate Cel Signatures. {parseError.Message}");
                }
                var celScriptSignatureInfo = parseResult.Data!;

                // Generate a source file for each CelScript 
                foreach (var kv in celScriptSignatureInfo.CelScriptSignatures)
                {
                    var celScriptName = kv.Key;
                    var celSignatures = kv.Value;

                    var buildResult = await GenerateSourceFile(celScriptName, celSignatures, celSignaturesFolder);
                    if (buildResult is ErrorResult<Assembly> buildError)
                    {
                        var error = new ErrorResult<string>($"Failed to generate Cel Signature source code. {buildError.Message}");
                        return error;
                    }
                }

                // Add a reference to the assembly containing the core Celbridge types
                var assemblyLocations = new List<string>()
                {
                    typeof(ExpressionBase).Assembly.Location
                };

                var compileResult = await AssemblyUtils.CompileAssembly(celSignaturesFolder, CelSignaturesName, assemblyLocations);
                if (compileResult is ErrorResult<string> compileError)
                {
                    return new ErrorResult<string>($"Failed to compile Cel Signatures assembly. {compileError.Message}");
                }
                var assemblyFile = compileResult.Data!;

                return new SuccessResult<string>(assemblyFile);
            }
            catch (Exception ex)
            {
                return new ErrorResult<string>($"Failed to update Cel Signatures. {ex.Message}");
            }
        }

        private async Task<Result<CelScriptSignatureInfo>> ParseCelScripts(string projectFolder)
        {
            if (!Directory.Exists(projectFolder))
            {
                return new ErrorResult<CelScriptSignatureInfo>($"Project folder not found: {projectFolder}");
            }

            try
            {
                // Contains all the parsed information for every CelScript in the project
                var celScriptSignatureInfo = new CelScriptSignatureInfo();

                string[] celFiles = Directory.GetFiles(projectFolder, "*.cel", SearchOption.AllDirectories);
                foreach (string celFile in celFiles)
                {
                    var celScriptName = Path.GetFileNameWithoutExtension(celFile);

                    // Contains the parsed info for a single Cel
                    var celSignatures = new List<CelSignatureInfo>();

                    // Parse the serialized CelScript json data
                    string fileContents = await File.ReadAllTextAsync(celFile);
                    JObject celScriptData = JObject.Parse(fileContents);

                    var cels = celScriptData["Cels"];
                    Guard.IsNotNull(cels);

                    JArray celsData = (JArray)cels;
                    if (celsData == null)
                    {
                        // The CelScript does not contain any Cels, so there's no need
                        // to generate CelSignatures for it.
                        continue;
                    }

                    foreach (var celData in celsData)
                    {
                        // Get the Cel Parameters
                        var celParameters = new List<CelParameterInfo>();

                        var inputData = celData["Input"];
                        Guard.IsNotNull(inputData);

                        var input = (JArray)inputData;
                        foreach (var instructionLine in input)
                        {
                            // The parameter type is the keyword with "Expression" appended.
                            // This works for the primitive types, we may need something more
                            // sophisticated for records later on.

                            var parameterName = (string)instructionLine["Instruction"]!["Name"]!;
                            var parameterType = (string)instructionLine["Keyword"]! + "Expression";

                            var celParameter = new CelParameterInfo()
                            {
                                Name = parameterName,
                                Type = parameterType,
                            };
                            celParameters.Add(celParameter);
                        }

                        // Get the Return Type
                        var output = (JArray)celData["Output"]!;
                        var returnType = string.Empty;
                        if (output.Count() > 0)
                        {
                            var o = output[0]["Keyword"];
                            if (o!.Type == JTokenType.String)
                            {
                                returnType = (string)o!;
                            }
                        }

                        // Get the Cel Name
                        var celName = (string)celData["Name"]!;

                        // Build a summary string which lists all the parameter & their values
                        bool first = true;
                        var sb = new StringBuilder();

                        sb.Append("(");
                        foreach (var celParameter in celParameters)
                        {
                            if (first)
                            {
                                first = false;
                            }
                            else
                            {
                                sb.Append(", ");
                            }

                            sb.Append(celParameter.Name);
                            sb.Append(": ");
                            sb.Append("{");
                            sb.Append(celParameter.Name);
                            sb.Append(".GetSummary(context)");
                            sb.Append("}");
                        }
                        sb.Append(")");

                        if (!string.IsNullOrEmpty(returnType))
                        {
                            sb.Append($" : {returnType}");
                        }

                        var summary = sb.ToString();

                        // Todo: Check for conflicting Cel definitions in this CelScript

                        // Add the Cel signature to the list of signatures
                        var celSignature = new CelSignatureInfo()
                        {
                            CelScriptName = celScriptName,
                            CelName = celName,
                            Parameters = celParameters,
                            ReturnType = returnType,
                            Summary = summary,
                        };
                        celSignatures.Add(celSignature);
                    }

                    // Todo: Check for conflicting CelScript definitions (e.g. same name, different folders)
                    celScriptSignatureInfo.CelScriptSignatures.Add(celScriptName, celSignatures);
                }
    
                return new SuccessResult<CelScriptSignatureInfo>(celScriptSignatureInfo);
            }
            catch (Exception ex)
            {
                return new ErrorResult<CelScriptSignatureInfo>($"Failed to parse Cel Scipts: {ex.Message}");
            }
        }

        private async Task<Result> GenerateSourceFile(string celScriptName, List<CelSignatureInfo> celSignatures, string signaturesFolder)
        {
            Guard.IsFalse(string.IsNullOrEmpty(celScriptName));

            var sourceFile = Path.Combine(signaturesFolder, $"{celScriptName}.cs");

            var templateText =
@"namespace Celbridge.Models.CelSignatures;

public record {{ cel_script_name }}
{
{{~ for signature in cel_signatures ~}}
    public record {{ signature.cel_name }} : ICelSignature
    {
    {{~ for parameter in signature.parameters ~}}
        public {{ parameter.type }} {{ parameter.name }} { get; set; } = new ();
    {{~ end ~}}    
        public string ReturnType => ""{{ signature.return_type }}"";
        public string GetSummary(PropertyContext context) => $""{{ signature.summary }}"";
    }
    {{~ if for.last == false ~}}
        {{~ ""\n"" ~}}
    {{~ end ~}}
{{~ end ~}}    
}";

            var template = Template.Parse(templateText);
            try
            {
                var result = template.Render(new
                {
                    CelScriptName = celScriptName,
                    CelSignatures = celSignatures
                });

                var directory = Path.GetDirectoryName(sourceFile);
                Guard.IsNotNull(directory);

                Directory.CreateDirectory(directory);

                await File.WriteAllTextAsync(sourceFile, result);
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to create Cel Signature source file. {ex.Message}");
            }

            return new SuccessResult();
        }

        private static string GetTypeName(ITypeInstruction typeInstruction)
        {
            string parameterType;
            switch (typeInstruction)
            {
                case PrimitivesMixin.Number _:
                    parameterType = nameof(NumberExpression);
                    break;
                case PrimitivesMixin.Boolean _:
                    parameterType = nameof(BooleanExpression);
                    break;
                case PrimitivesMixin.String _:
                    parameterType = nameof(StringExpression);
                    break;
                default:
                    parameterType = nameof(VariableExpression);
                    break;
            }

            return parameterType;
        }
    }
}
