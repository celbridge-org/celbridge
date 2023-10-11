namespace Celbridge.Models
{
    record InstructionDetailsChangedMessage(InstructionLine instructionLine);

    // A container for an IInstruction
    [InstructionLineProperty]
    [ShowDetailOnSelect]
    public record InstructionLine : IRecord, ITreeNode
    {
        public ITreeNodeRef ParentNode { get; } = new ParentNodeRef();
        public void OnSetParent(ITreeNode parentNode)
        {
            // Set this InstructionLine as the parent of the Instruction
            if (Instruction is ITreeNode childNode)
            {
                ParentNodeRef.SetParent(childNode, this);
            }
        }

        public string Keyword { get; set; }
        public IInstruction Instruction { get; set; } = new EmptyInstruction();

        public override string ToString() => Instruction?.ToString();
    }
}
