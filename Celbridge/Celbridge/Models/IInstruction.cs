using Newtonsoft.Json;

namespace Celbridge.Models
{
    public enum PipeState
    {
        NotConnected,
        PipeConsumer,
        PipeProducer
    }

    public enum SummaryFormat
    {
        PlainText,
        CSharpExpression
    }

    public record InstructionSummary(SummaryFormat SummaryFormat, string SummaryText);

    // An editable and serializable instruction
    public interface IInstruction : IRecord
    {
        [JsonIgnore]
        InstructionCategory InstructionCategory { get; }

        [JsonIgnore]
        IndentModifier IndentModifier { get; }

        [JsonIgnore]
        PipeState PipeState { get; set; }

        InstructionSummary GetInstructionSummary(PropertyContext Context);
    }

    // An instruction that can be used as a type in Celbridge 
    public interface ITypeInstruction
    {
        string Name { get; }

        ExpressionBase GetExpression();
    }
}
