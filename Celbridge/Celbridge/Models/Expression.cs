using Celbridge.Utils;
using Newtonsoft.Json;

namespace Celbridge.Models
{
    public abstract record ExpressionBase : IRecord
    {
        public string Expression { get; set; } = string.Empty;

        public virtual string GetSummary(PropertyContext context = PropertyContext.CelInstructions) => Expression;

        public override string ToString()
        {
            return Expression ?? string.Empty;
        }
    }

    // Expression must evaluate to a Number literal or variable
    public record NumberExpression : ExpressionBase
    {
        public override string GetSummary(PropertyContext context = PropertyContext.CelInstructions)
        {
            // Single words are assumed to be identifiers (if valid)
            if (StringUtils.IsValidCSharpExpression(Expression))
            {
                return Expression;
            }

            return default(double).ToString();
        }
    }

    // Expression must evaluate to a Boolean literal or variable
    public record BooleanExpression : ExpressionBase
    {
        public override string GetSummary(PropertyContext context = PropertyContext.CelInstructions)
        {
            // Single words are assumed to be identifiers (if valid)
            if (StringUtils.IsValidCSharpExpression(Expression))
            {
                return Expression;
            }

            return default(bool).ToString();
        }
    }

    // Expression must evaluate to a string literal or variable
    public record StringExpression : ExpressionBase
    {
        public override string GetSummary(PropertyContext context = PropertyContext.CelInstructions)
        {
            // Single words are assumed to be identifiers (if valid)
            if (StringUtils.IsValidCSharpIdentifier(Expression))
            {
                return Expression;
            }

            var escaped = JsonConvert.ToString(Expression);
            if (escaped.IndexOf('{') != -1)
            {
                // String is probably interpolated so prepend $
                escaped = "$" + escaped;
            }

            // Always returns a quoted string.
            return escaped;
        }
    }

    // Expression must reference a single variable (used for passing Records)
    public record VariableExpression : ExpressionBase
    {}

    // Expression may reference any type of literal or variable
    public record AnyExpression : ExpressionBase
    {}
}
