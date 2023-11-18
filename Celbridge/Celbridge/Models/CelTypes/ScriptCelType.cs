using Celbridge.Models.CelMixins;

namespace Celbridge.Models.CelTypes
{   
    public class ScriptCelType : ICelType
    {
        public string Name => "Script";
        public string Description => "General purpose script Cel";
        public string Icon => "\uE14C"; // List icon
        public string Color => "#E0B152";

        public List<ICelMixin> CelMixins { get; } = CreateStandardMixins();

        public static List<ICelMixin> CreateStandardMixins()
        {
            return new()
            {
                new PrimitivesMixin(),
                new BasicMixin(),
                new FileMixin(),
                new ChatMixin(),
                new MarkdownMixin(),
                new SheetMixin(),
            };
        }
    }
}
