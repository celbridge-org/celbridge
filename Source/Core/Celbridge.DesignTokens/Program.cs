using System.Text;

namespace Celbridge.DesignTokens;

/// <summary>
/// Generates the colour dictionary and the token stylesheet from the token source. Run from the build,
/// so a failure here is reported as a build error and the generated files are never half written.
/// </summary>
public static class Program
{
    private const string SourceArgument = "--source";
    private const string XamlArgument = "--xaml";
    private const string CssArgument = "--css";

    public static int Main(string[] arguments)
    {
        try
        {
            var options = ParseArguments(arguments);
            var source = DesignTokenSourceLoader.LoadFromFile(options.SourcePath);

            WriteGeneratedFile(options.XamlPath, XamlTokenWriter.Write(source));
            WriteGeneratedFile(options.CssPath, CssTokenWriter.Write(source));

            var tokenCount = source.Tokens.Count();
            Console.WriteLine($"Generated {tokenCount} design tokens.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Design token generation failed: {exception.Message}");

            return 1;
        }
    }

    private static GeneratorOptions ParseArguments(string[] arguments)
    {
        string? sourcePath = null;
        string? xamlPath = null;
        string? cssPath = null;

        for (var index = 0; index < arguments.Length - 1; index += 2)
        {
            var name = arguments[index];
            var value = arguments[index + 1];

            switch (name)
            {
                case SourceArgument:
                    sourcePath = value;
                    break;

                case XamlArgument:
                    xamlPath = value;
                    break;

                case CssArgument:
                    cssPath = value;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{name}'.");
            }
        }

        if (sourcePath is null ||
            xamlPath is null ||
            cssPath is null)
        {
            throw new ArgumentException(
                $"Usage: {SourceArgument} <path> {XamlArgument} <path> {CssArgument} <path>");
        }

        return new GeneratorOptions(sourcePath, xamlPath, cssPath);
    }

    // The repository is LF throughout and the web tooling reads the stylesheet, so both files are written
    // with newlines as authored and no byte order mark.
    private static void WriteGeneratedFile(string path, string content)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(path, content, encoding);
    }

    private sealed record GeneratorOptions(string SourcePath, string XamlPath, string CssPath);
}
