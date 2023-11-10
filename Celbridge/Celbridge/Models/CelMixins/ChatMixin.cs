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

            public override Result<string> GenerateCode()
            {
                var context = Context.GetSummary();
                var code = $"_env.Chat.StartChat({context});";
                return new SuccessResult<string>(code);
            }
        }

        public record Ask : InstructionBase
        {
            public StringExpression Question { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override string ReturnType => nameof(PrimitivesMixin.String);


            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Question.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var summary = Question.GetSummary();
                var code = $"await _env.Chat.Ask({summary});";
                return new SuccessResult<string>(code);
            }
        }

        public record EndChat : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Chat.EndChat();";
                return new SuccessResult<string>(code);
            }
        }
    }
}
