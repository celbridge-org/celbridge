namespace Celbridge.Models
{
    public record EmptyInstruction : InstructionBase
    {
        public override string ToString() => string.Empty;
    }
}
