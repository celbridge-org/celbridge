using Celbridge.Models.CelMixins;

namespace Celbridge.Models.CelTypes
{
    public class StartCelType : ICelType
    {
        public string Name => "Start";
        public string Description => "An entry point for running the application";
        public string Icon => "\uE102"; // Play icon
        public string Color => "#52E052";

        public List<ICelMixin> CelMixins { get; } = BasicCelType.CreateStandardMixins();
    }
}
