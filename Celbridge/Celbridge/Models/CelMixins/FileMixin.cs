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
            public StringExpression Resource { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"Resource: {Resource}");
            }

            public override Result<string> GenerateCode()
            {
                var code = $"_env.TextFile.ReadText(\"{Resource}\");";
                return new SuccessResult<string>(code);
            }
        }

        public record Write : InstructionBase
        {
            public StringExpression Resource { get; set; } = new();

            public StringExpression Text { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"Resource : {Resource.GetSummary()}, Text: {Text.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var resource = Resource.GetSummary();
                var text = Text.GetSummary();
                var code = $"_env.TextFile.WriteText({resource}, {text});";
                return new SuccessResult<string>(code);
            }
        }
    }
}
