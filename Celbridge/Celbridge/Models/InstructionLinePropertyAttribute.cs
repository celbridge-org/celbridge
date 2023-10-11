namespace Celbridge.Models
{
    public class InstructionLinePropertyAttribute : RecordPropertyAttribute
    {
        public InstructionLinePropertyAttribute()
            : base("Celbridge.Views.InstructionLinePropertyView", typeof(InstructionLine))
        {}
    }
}

