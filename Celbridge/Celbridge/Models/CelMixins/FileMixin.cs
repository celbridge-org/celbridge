namespace Celbridge.Models.CelMixins
{
    public class FileMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(Read), typeof(Read) },
            { nameof(Write), typeof(Write) },
        };

        public record Read : InstructionBase
        {
            public string Resource { get; set; } = string.Empty;

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: Resource);
            }
        }

        public record Write : InstructionBase
        {
            public string Resource { get; set; } = string.Empty;

            public StringExpression Text { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Resource} : {Text.Expression}");
            }
        }
    }
}
