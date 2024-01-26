using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CelLegacy.Utils;

public static class StringUtils
{
    public static string ToHumanFromPascal(this string s)
    {
        if (2 > s.Length)
        {
            return s.ToUpperInvariant();
        }

        var sb = new StringBuilder();
        var ca = s.ToCharArray();
        sb.Append(char.ToUpperInvariant(ca[0]));
        for (int i = 1; i < ca.Length - 1; i++)
        {
            char c = ca[i];
            if (char.IsUpper(c) && (char.IsLower(ca[i + 1]) || char.IsLower(ca[i - 1])))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        sb.Append(ca[ca.Length - 1]);
        return sb.ToString();
    }

    public static Result<string> ExtractBraceContent(string input)
    {
        int startIndex = input.IndexOf('{');
        if (startIndex == -1)
        {
            return new ErrorResult<string>("Input doesn't contain an opening brace '{'.");
        }

        int endIndex = input.LastIndexOf('}');
        if (endIndex == -1)
        {
            return new ErrorResult<string>("Input doesn't contain a closing brace '}'.");
        }

        if (endIndex <= startIndex)
        {
            return new ErrorResult<string>("Closing brace '}' appears before opening brace '{'.");
        }

        try
        {
            var content = input.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
            return new SuccessResult<string>(content);
        }
        catch (Exception ex)
        {
            return new ErrorResult<string>($"Failed to extract brace content. {ex.Message}");
        }
    }

    public static bool IsValidCSharpIdentifier(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        var identifierNode = SyntaxFactory.ParseName(input);
        if (identifierNode is IdentifierNameSyntax identifierSyntax)
        {
            var token = identifierSyntax.ToString();
            return token == input;
        }

        return false;
    }

    public static bool IsValidCSharpExpression(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        // Use Roslyn to parse the input string as a C# expression
        var root = SyntaxFactory.ParseExpression(input);
        var diagnostics = root.GetDiagnostics();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }
}
