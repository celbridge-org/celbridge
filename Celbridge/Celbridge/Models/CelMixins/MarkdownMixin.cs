namespace Celbridge.Models.CelMixins
{
    public class MarkdownMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(ClearMarkdown), typeof(ClearMarkdown) },
            { nameof(StartSection), typeof(StartSection) },
            { nameof(EndSection), typeof(EndSection) },
            { nameof(AddLine), typeof(AddLine) },
            { nameof(AddComment), typeof(AddComment) },
            { nameof(AddSeparator), typeof(AddSeparator) },
            { nameof(SetBackground), typeof(SetBackground) },
            { nameof(GetMarkdown), typeof(GetMarkdown) },
        };

        public record ClearMarkdown : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Markdown.ClearMarkdown();";
                return new SuccessResult<string>(code);
            }
        }

        public record StartSection : InstructionBase
        {
            public StringExpression Title { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;
            public override IndentModifier IndentModifier => IndentModifier.PostIncrement;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Title.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var title = Title.GetSummary();
                var code = $"_env.Markdown.StartSection({title});";
                return new SuccessResult<string>(code);
            }
        }

        public record EndSection : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;
            public override IndentModifier IndentModifier => IndentModifier.PreDecrement;

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Markdown.EndSection();";
                return new SuccessResult<string>(code);
            }
        }

        public record AddLine : InstructionBase
        {
            public StringExpression MarkdownText { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{MarkdownText.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var markdownText = MarkdownText.GetSummary();
                var code = $"_env.Markdown.AddLine({markdownText});";
                return new SuccessResult<string>(code);
            }
        }

        public record AddComment : InstructionBase
        {
            public StringExpression Comment { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{Comment.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var comment = Comment.GetSummary();
                var code = $"_env.Markdown.AddComment({comment});";
                return new SuccessResult<string>(code);
            }
        }

        public record AddSeparator : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Markdown.AddSeparator();";
                return new SuccessResult<string>(code);
            }
        }

        public record SetBackground : InstructionBase
        {
            public StringExpression ImageResource { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{ImageResource.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var imageResource = ImageResource.GetSummary();
                var code = $"_env.Markdown.SetBackground({imageResource});";
                return new SuccessResult<string>(code);
            }
        }

        public record GetMarkdown : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override string ReturnType => nameof(PrimitivesMixin.String);

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Markdown.GetMarkdown();";
                return new SuccessResult<string>(code);
            }
        }
    }
}
