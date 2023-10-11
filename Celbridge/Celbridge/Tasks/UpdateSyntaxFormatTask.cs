using Celbridge.Utils;
using Celbridge.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tasks
{
    public record SyntaxFormat(
        InstructionCategory Category, 
        IndentModifier IndentModifier, 
        int IndentLevel,
        PipeState PipeState,
        int HashCode, 
        SyntaxState State, 
        string Summary, 
        string Tooltip,
        string ErrorMessage, 
        List<SyntaxToken> Tokens);

    public record CelSyntaxFormat
    {
        public List<SyntaxFormat> InputSyntaxFormat = new();
        public List<SyntaxFormat> OutputSyntaxFormat = new();
        public List<SyntaxFormat> InstructionSyntaxFormat = new ();

        public void Clear()
        {
            InputSyntaxFormat.Clear();
            OutputSyntaxFormat.Clear();
            InstructionSyntaxFormat.Clear();
        }
    }

    public record CelSyntaxFormatUpdatedMessage(CelSyntaxFormat CelSyntaxFormat);

    public class SyntaxToken
    {
        public InstructionCategory Category { get; set; }
        public string Text { get; set; }
    }

    public class UpdateSyntaxFormatTask : IDisposable
    {
        private ICel _cel;

        private CelSyntaxFormat _celSyntaxFormat = new ();

        private Dictionary<int, SyntaxFormat> _cachedSyntaxFormat = new ();

        private readonly CancellationTokenSource _cancellationToken = new();

        public event Action<CelSyntaxFormat> CelSyntaxFormatUpdated;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose of any managed objects here.
                _cancellationToken.Cancel();
            }

            // Dispose of any unmanaged objects here.
        }

        public async Task Start(ICel cel)
        {
            Guard.IsNotNull(cel);
            _cel = cel;

            while (!_cancellationToken.Token.IsCancellationRequested)
            {
                UpdateSyntaxFormat();
                await Task.Delay(100);
            }
        }

        public void UpdateSyntaxFormat()
        {
            // Todo: Use a double buffer here and update syntax on a separate thread. Make Instructions list immutable?
            _celSyntaxFormat.Clear();

            // Todo: Handle exceptions gracefully when processing instruction lines
            ProcessInstructionLines(_cel.Input, _celSyntaxFormat.InputSyntaxFormat, PropertyContext.CelInput);
            ProcessInstructionLines(_cel.Output, _celSyntaxFormat.OutputSyntaxFormat, PropertyContext.CelOutput);
            ProcessInstructionLines(_cel.Instructions, _celSyntaxFormat.InstructionSyntaxFormat, PropertyContext.CelInstructions);

            // Todo: (Optimization) Only notify listeners if the syntax format list has actually changed, and include a list of lines that have changed.
            CelSyntaxFormatUpdated?.Invoke(_celSyntaxFormat);
        }

        private void ProcessInstructionLines(List<InstructionLine> instructionLines, List<SyntaxFormat> syntaxFormatLines, PropertyContext context)
        {
            int indentLevel = 0;
            foreach (var instructionLine in instructionLines)
            {
                if (instructionLine.Instruction is not null)
                {
                    var instruction = instructionLine.Instruction;

                    // Cache format info based on the instruction hash.
                    // This works because record hashes are based on all the instruction's properties.
                    var hashCode = instruction.GetHashCode();
                    if (!_cachedSyntaxFormat.TryGetValue(hashCode, out var syntaxFormat))
                    {
                        syntaxFormat = ProcessInstruction(instruction, context);
                        _cachedSyntaxFormat[hashCode] = syntaxFormat;
                    }

                    // Apply indent offset BEFORE updating the line's indent level
                    switch (syntaxFormat.IndentModifier)
                    {
                        case IndentModifier.PreIncrement:
                            indentLevel++;
                            break;
                        case IndentModifier.PreDecrement:
                        case IndentModifier.PreDecrementPostIncrement:
                            indentLevel--;
                            break;
                    }

                    indentLevel = Math.Max(indentLevel, 0);

                    // Add the syntax format to the list
                    if (syntaxFormat.State == SyntaxState.Error)
                    {
                        syntaxFormatLines.Add(syntaxFormat with
                        {
                            IndentLevel = indentLevel,
                            State = SyntaxState.Error,
                            ErrorMessage = syntaxFormat.ErrorMessage,
                        });
                    }
                    else
                    {
                        syntaxFormatLines.Add(syntaxFormat with
                        {
                            IndentLevel = indentLevel,
                            State = SyntaxState.Valid,
                            PipeState = instruction.PipeState,
                        });
                    }

                    // Apply indent offset AFTER setting the current line's indent level
                    switch (syntaxFormat.IndentModifier)
                    {
                        case IndentModifier.PostIncrement:
                        case IndentModifier.PreDecrementPostIncrement:
                            indentLevel++;
                            break;
                        case IndentModifier.PostDecrement:
                            indentLevel--;
                            break;
                    }
                    indentLevel = Math.Max(indentLevel, 0);
                }
            }
        }

        private SyntaxFormat ProcessInstruction(IInstruction instruction, PropertyContext context)
        {
            Guard.IsNotNull(instruction);

            var instructionSummary = instruction.GetInstructionSummary(context);

            ExpressionInfo expressionInfo = null;
            if (instructionSummary.SummaryFormat == SummaryFormat.CSharpExpression)
            {
                expressionInfo = ExpressionParser.Parse(instructionSummary.SummaryText);
            }
            else
            {
                expressionInfo = new ExpressionInfo()
                {
                    State = SyntaxState.Valid,
                };                                                                         
                expressionInfo.Tokens.Add(new SyntaxToken()
                {
                    Category = InstructionCategory.Text,
                    Text = instructionSummary.SummaryText,
                });
            }

            var syntaxFormat = new SyntaxFormat(
                Category: instruction.InstructionCategory,
                IndentModifier: instruction.IndentModifier,
                IndentLevel: 0,
                PipeState: PipeState.NotConnected,
                HashCode: instruction.GetHashCode(),
                State: SyntaxState.Unknown,
                Summary: instructionSummary.SummaryText,
                Tooltip: instruction.Description,
                ErrorMessage: string.Empty,
                Tokens: expressionInfo.Tokens
            );

            if (expressionInfo.State == SyntaxState.Error)
            {
                return syntaxFormat with
                {
                    State = SyntaxState.Error,
                    Summary = instructionSummary.SummaryText,
                    ErrorMessage = expressionInfo.ErrorMessage,
                };
            }

            return syntaxFormat;
        }
    }
}
