namespace Celbridge.Models.CelMixins
{
    public class ChatMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(StartChat), typeof(StartChat) },
            { nameof(Ask), typeof(Ask) },
            { nameof(EndChat), typeof(EndChat) },
        };

        public record StartChat : InstructionBase
        {
            public StringExpression Context { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Context.GetSummary()}");
            }
        }

        public record Ask : InstructionBase
        {
            public StringExpression Question { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Question.GetSummary()}");
            }
        }

        public record EndChat : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;
        }
    }
}
