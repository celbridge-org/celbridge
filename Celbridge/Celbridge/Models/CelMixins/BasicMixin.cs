namespace Celbridge.Models.CelMixins
{
    public class BasicMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(Print), typeof(Print) },
            { nameof(If), typeof(If) },
            { nameof(Else), typeof(Else) },
            { nameof(End), typeof(End) },
            { nameof(Return), typeof(Return) },
            { nameof(While), typeof(While) },
            { nameof(Set), typeof(Set) },
            { nameof(Call), typeof(Call) },
            { nameof(StartProcess), typeof(StartProcess) },
        };

        public record Print : InstructionBase
        {
            public StringExpression Message { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Message.GetSummary()}");
            }
        }

        public record If : InstructionBase
        {
            public BooleanExpression Condition { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.ControlFlow;
            public override IndentModifier IndentModifier => IndentModifier.PostIncrement;

            public override InstructionSummary GetInstructionSummary(PropertyContext Context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.CSharpExpression,
                    SummaryText: Condition.Expression);
            }
        }

        public record Else : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.ControlFlow;
            public override IndentModifier IndentModifier => IndentModifier.PreDecrementPostIncrement;
        }

        public record End : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.ControlFlow;
            public override IndentModifier IndentModifier => IndentModifier.PreDecrement;
        }

        public record Return : InstructionBase
        {
            public AnyExpression Result { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.ControlTransfer;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.CSharpExpression,
                    SummaryText: Result.Expression);
            }
        }

        public record While : InstructionBase
        {
            public BooleanExpression Condition { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.ControlFlow;
            public override IndentModifier IndentModifier => IndentModifier.PostIncrement;

            public override InstructionSummary GetInstructionSummary(PropertyContext Context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.CSharpExpression,
                    SummaryText: Condition.Expression);
            }
        }

        public record Set : InstructionBase, ITypeInstruction
        {
            public string Name { get; set; } = string.Empty;
            public AnyExpression Expression { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.Declaration;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                if (context == PropertyContext.CelInstructions)
                {
                    if (PipeState == PipeState.PipeConsumer)
                    {
                        return new InstructionSummary(
                                SummaryFormat: SummaryFormat.PlainText,
                                SummaryText: $"{Name} =");
                    }

                    var expressionSummary = Expression.GetSummary(context);
                    return new InstructionSummary(
                            SummaryFormat: SummaryFormat.CSharpExpression,
                            SummaryText: $"{Name} = {expressionSummary}");
                }
                return base.GetInstructionSummary(context);
            }

            public ExpressionBase GetExpression()
            {
                return Expression;
            }
        }

        public record Call : InstructionBase
        {
            public override void OnSetParent(ITreeNode parentNode)
            {
                ParentNodeRef.SetParent(Arguments, this);
            }

            [CallArgumentsProperty]
            public CallArguments Arguments { get; set; } = new CallArguments();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: Arguments.GetSummary(context));
            }

            /*
            public string GetInstructionSummary(PropertyContext Context)
            {
                var sb = new StringBuilder();
                sb.Append(TargetCel);
                sb.Append("(");

                bool firstArg = true;
                foreach (var arg in Arguments)
                {
                    if (firstArg)
                    {
                        firstArg = false;
                    }
                    else
                    {
                        sb.Append(", ");
                    }
                    sb.Append(arg.Name);
                    sb.Append(":");
                    sb.Append(arg.Expression);
                }

                sb.Append(")");
                return sb.ToString();
            }

            // Todo: Implement a custom CelList that overrides GetHashCode to return a hash of all its elements
            // Override GetHashCode to give a different hash for different argument values.
            public override int GetHashCode()
            {
                unchecked
                {
                    int result = 37; // prime

                    result *= 397; // also prime (see note)
                    if (CelName != null)
                        result += CelName.GetHashCode();

                    foreach (var argument in Arguments)
                    {
                        result *= 397;
                        result += argument.GetHashCode();
                    }

                    return result;
                }
            }
            */
        }

        public record StartProcess : InstructionBase
        {
            public StringExpression Executable { get; set; } = new();

            // Todo: Support a list of arguments
            public StringExpression Arguments { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Executable.Expression} {Arguments.Expression}");
            }
        }
    }
}
