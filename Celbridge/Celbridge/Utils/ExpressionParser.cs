using System.Collections.Generic;
using Celbridge.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Celbridge.Utils
{
    public record ExpressionInfo
    {
        public List<Tasks.SyntaxToken> Tokens { get; } = new List<Tasks.SyntaxToken>();
        public SyntaxState State { get; init; }
        public string ErrorMessage { get; init; }

    }

    public static class ExpressionParser
    {
        public static ExpressionInfo Parse(string expressionText)
        {
            if (string.IsNullOrEmpty(expressionText))
            {
                return new ExpressionInfo
                {
                    State = SyntaxState.Error,
                    ErrorMessage = "Expression is empty"
                };
            }

            try
            {
                return ParseInternal(expressionText);
            }
            catch (System.Exception ex)
            {
                return new ExpressionInfo
                {
                    State = SyntaxState.Error,
                    ErrorMessage = ex.Message
                };
            } 
        }

        private static ExpressionInfo ParseInternal(string expressionText)
        {
            var expressionInfo = new ExpressionInfo();

            ExpressionSyntax root = SyntaxFactory.ParseExpression(expressionText);

            var diagnostics = root.GetDiagnostics();
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    // Report the first error encountered
                    var location = diagnostic.Location;
                    expressionInfo = expressionInfo with 
                    { 
                        State = SyntaxState.Error,
                        ErrorMessage = $"Error {diagnostic.Id} at character {location.SourceSpan.Start}"
                    };
                    break;
                } 
            }

            foreach (SyntaxToken syntaxToken in root.DescendantTokens())
            {
                InstructionCategory category;
                switch (syntaxToken.Kind())
                {
                    case SyntaxKind.IdentifierToken:
                        category = InstructionCategory.Identifier;
                        break;
                    case SyntaxKind.StringLiteralToken:
                        category = InstructionCategory.String;
                        break;
                    case SyntaxKind.NumericLiteralToken:
                        category = InstructionCategory.Number;
                        break;
                    case SyntaxKind.ColonToken:
                    case SyntaxKind.CommaToken:
                    case SyntaxKind.EqualsToken:
                    case SyntaxKind.PlusToken:
                    case SyntaxKind.MinusToken:
                    case SyntaxKind.AsteriskToken:
                    case SyntaxKind.SlashToken:
                    case SyntaxKind.LessThanToken:
                    case SyntaxKind.GreaterThanToken:
                    case SyntaxKind.EqualsEqualsToken:
                    case SyntaxKind.LessThanEqualsToken:
                    case SyntaxKind.GreaterThanEqualsToken:
                        category = InstructionCategory.Operator;
                        break;
                    case SyntaxKind.OpenParenToken:
                    case SyntaxKind.CloseParenToken:
                        category = InstructionCategory.Parenthesis;
                        break;
                    default:
                        category = InstructionCategory.Error;
                        break;
                }

                string text;
                if (category == InstructionCategory.Operator)
                {
                    // Add whitespace around operators to aid readability of erxpression
                    text = $" {syntaxToken} ";
                }
                else
                {
                    text = syntaxToken.ToString();
                }

                var token = new Tasks.SyntaxToken()
                {
                    Category = category,
                    Text = text,
                };

                expressionInfo.Tokens.Add(token);
            }

            return expressionInfo;
        }
    }
}
