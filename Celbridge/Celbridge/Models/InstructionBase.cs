using Celbridge.Utils;
using Newtonsoft.Json;

namespace Celbridge.Models
{
    public abstract record InstructionBase : IInstruction, ITreeNode
    {
        public ITreeNodeRef ParentNode { get; } = new ParentNodeRef();
        public virtual void OnSetParent(ITreeNode parentNode)
        {}

        public virtual InstructionCategory InstructionCategory => InstructionCategory.Text;

        public virtual IndentModifier IndentModifier => IndentModifier.NoChange;

        public virtual PipeState PipeState { get; set; }

        public virtual string Description
        {
            get
            {
                JsonSerializerSettings settings = new()
                {
                    TypeNameHandling = TypeNameHandling.None,
                    Formatting = Formatting.Indented,
                };
                string json = JsonConvert.SerializeObject(this, settings);

                // Todo: Process the Json text to make a cleaner looking tooltip

                var contentResult = StringUtils.ExtractBraceContent(json);
                if (contentResult.Failure)
                {
                    return json;
                }
                var content = contentResult.Data;

                return content;
            }
        }

        public virtual InstructionSummary GetInstructionSummary(PropertyContext Context)
        {
            return new InstructionSummary(
                SummaryFormat: SummaryFormat.PlainText,
                SummaryText: string.Empty);
        }
    }
}
