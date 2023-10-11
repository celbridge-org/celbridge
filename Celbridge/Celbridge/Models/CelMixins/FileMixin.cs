using Celbridge.Utils;
using System;
using System.Collections.Generic;

namespace Celbridge.Models.CelMixins
{
    public class FileMixin : ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; } = new()
        {
            { nameof(ReadText), typeof(ReadText) },
            { nameof(WriteText), typeof(WriteText) },
        };

        public record ReadText : InstructionBase
        {
            public string Resource { get; set; }

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: Resource);
            }
        }

        public record WriteText : InstructionBase
        {
            public string Resource { get; set; }

            public override InstructionCategory InstructionCategory => InstructionCategory.FunctionCall;

            public override InstructionSummary GetInstructionSummary(PropertyContext context)
            {
                return new InstructionSummary(
                    SummaryFormat: SummaryFormat.PlainText,
                    SummaryText: Resource);
            }
        }
    }
}
