using Celbridge.Models;
using Microsoft.UI;
using System.Collections.Generic;
using Windows.UI;

namespace Celbridge.Utils
{
    public class SyntaxColors
    {
        private Dictionary<InstructionCategory, Color> _colors = new()
        {
            { InstructionCategory.Text, Colors.Beige },
            { InstructionCategory.Error, Colors.Red },
            { InstructionCategory.Comment, Colors.LightGreen },
            { InstructionCategory.Identifier, Colors.LightGoldenrodYellow },
            { InstructionCategory.Operator, Colors.LightCyan },
            { InstructionCategory.Separator, Colors.LightGray },
            { InstructionCategory.Number, Colors.DarkSeaGreen },
            { InstructionCategory.Boolean, Colors.AliceBlue },
            { InstructionCategory.String, Colors.Beige },
            { InstructionCategory.Parenthesis, Colors.Wheat },
            { InstructionCategory.Expression, Colors.LightPink },
            { InstructionCategory.ControlFlow, Colors.SkyBlue },
            { InstructionCategory.ControlTransfer, Colors.MediumAquamarine },
            { InstructionCategory.Scope, Colors.Magenta },
            { InstructionCategory.Declaration, Colors.LightPink },
            { InstructionCategory.FunctionCall, Colors.MediumAquamarine },
        };


        public Result<Color> GetColor(InstructionCategory category)
        {
            if (_colors.TryGetValue(category, out Color lookupColor))
            {
                return new SuccessResult<Color>(lookupColor);
            }

            if (_colors.TryGetValue(default(InstructionCategory), out Color defaultColor))
            {
                return new SuccessResult<Color>(lookupColor);
            }

            return new ErrorResult<Color>($"Failed to find color for category: {category}");
        }
    }
}
