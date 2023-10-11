using System;
using System.Collections.Generic;

namespace Celbridge.Models.CelMixins
{
    public class PrimitivesMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(String), typeof(String) },
            { nameof(Boolean), typeof(Boolean) },
            { nameof(Number), typeof(Number) },
        };

        public record String : TypeInstructionBase
        {
            public override ExpressionBase GetExpression() => Expression;
            public StringExpression Expression { get; set; } = new ();

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
        }

        public record Boolean : TypeInstructionBase
        {
            public override ExpressionBase GetExpression() => Expression;
            public BooleanExpression Expression { get; set; } = new ();

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
        }

        public record Number : TypeInstructionBase
        {
            // Todo: Support a NumberType property (long, double, etc - like Affinity in Unity ECS)
            public override ExpressionBase GetExpression() => Expression;
            public NumberExpression Expression { get; set; } = new ();

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
        }
    }
}
