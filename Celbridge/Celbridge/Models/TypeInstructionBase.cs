namespace Celbridge.Models
{
    public abstract record TypeInstructionBase : InstructionBase, ITypeInstruction, IEditable
    {
        public string Name { get; set; }
        public abstract ExpressionBase GetExpression();

        public override InstructionCategory InstructionCategory => InstructionCategory.Declaration;

        public override InstructionSummary GetInstructionSummary(PropertyContext context)
        {
            if (context == PropertyContext.CelInput)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: Name);
            }

            if (context == PropertyContext.CelOutput)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText, 
                    SummaryText: string.Empty);
            }

            return new InstructionSummary(
                SummaryFormat: SummaryFormat.PlainText, 
                SummaryText: string.Empty);
        }

        public PropertyEditMode GetPropertyEditMode(PropertyContext context, string propertyName)
        {
            if (context == PropertyContext.CelOutput)
            {
                return PropertyEditMode.Hide;
            }

            if (context == PropertyContext.CelInput &&
                propertyName != nameof(Name))
            {
                return PropertyEditMode.Hide;
            }

            if (context == PropertyContext.CelInstructions &&
                propertyName == "Expression" &&
                PipeState == PipeState.PipeConsumer)
            {
                // Hide the Expression on variable declarations that are connected to a Call
                return PropertyEditMode.Hide;
            }

            return PropertyEditMode.EditEnabled;
        }
    }
}
