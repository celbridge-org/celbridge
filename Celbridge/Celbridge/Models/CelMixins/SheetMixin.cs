namespace Celbridge.Models.CelMixins
{
    public class SheetMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(ReadSheet), typeof(ReadSheet) },
            { nameof(ClearSheet), typeof(ClearSheet) },
            { nameof(GetNumRows), typeof(GetNumRows) },
            { nameof(GetNumColumns), typeof(GetNumColumns) },
            { nameof(GetString), typeof(GetString) },
            { nameof(GetNumber), typeof(GetNumber) },
        };

        public record ReadSheet : InstructionBase
        {
            public StringExpression SheetName { get; set; } = new();

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"{SheetName.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var sheetName = SheetName.GetSummary();
                var code = $"_env.Sheet.ReadSheet({sheetName});";
                return new SuccessResult<string>(code);
            }
        }

        public record ClearSheet : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Sheet.Clear();";
                return new SuccessResult<string>(code);
            }
        }

        public record GetNumRows : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override string ReturnType => nameof(PrimitivesMixin.Number);

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Sheet.GetNumRows();";
                return new SuccessResult<string>(code);
            }
        }

        public record GetNumColumns : InstructionBase
        {
            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override string ReturnType => nameof(PrimitivesMixin.Number);

            public override Result<string> GenerateCode()
            {
                var code = $"_env.Sheet.GetNumColumns();";
                return new SuccessResult<string>(code);
            }
        }

        public record GetString : InstructionBase
        {
            public NumberExpression RowIndex { get; set; } = new();
            public NumberExpression ColumnIndex { get; set; } = new();

            public override string ReturnType => nameof(PrimitivesMixin.String);

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"RowIndex: {RowIndex.GetSummary()}, ColumnIndex: {ColumnIndex.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var rowIndex = RowIndex.GetSummary();
                var columnIndex = ColumnIndex.GetSummary();
                var code = $"_env.Sheet.GetString({rowIndex}, {columnIndex});";
                return new SuccessResult<string>(code);
            }
        }

        public record GetNumber : InstructionBase
        {
            public NumberExpression RowIndex { get; set; } = new();
            public NumberExpression ColumnIndex { get; set; } = new();
            public override string ReturnType => nameof(PrimitivesMixin.Number);

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: $"RowIndex: {RowIndex.GetSummary()}, ColumnIndex: {ColumnIndex.GetSummary()}");
            }

            public override Result<string> GenerateCode()
            {
                var rowIndex = RowIndex.GetSummary();
                var columnIndex = ColumnIndex.GetSummary();
                var code = $"_env.Sheet.GetNumber({rowIndex}, {columnIndex});";
                return new SuccessResult<string>(code);
            }
        }
    }
}
