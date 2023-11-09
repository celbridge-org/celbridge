using Celbridge.Models.CelMixins;

namespace Celbridge.Models.CelTypes
{
    public class TestCelType : ICelType
    {
        public string Name => "Test";
        public string Description => "An entry point for running a test";
        public string Icon => "Play";
        public string Color => "#E05252";

        public List<ICelMixin> CelMixins { get; } = BasicCelType.CreateStandardMixins();
    }
}
