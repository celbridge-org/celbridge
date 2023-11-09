using Celbridge.Models.CelMixins;

namespace Celbridge.Models.CelTypes
{   
    public class BasicCelType : ICelType
    {
        public string Name => "Basic";
        public string Description => "General purpose instructions";
        public string Icon => "PostUpdate";
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
            };
        }
    }
}
