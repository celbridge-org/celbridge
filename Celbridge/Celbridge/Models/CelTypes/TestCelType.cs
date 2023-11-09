namespace Celbridge.Models.CelTypes
{
    public class TestCelType : ICelType
    {
        public string Name => "Test";
        public string Description => "An entry point for running a test";
        public string Icon => "\uE10B"; // Accept icon
        public string Color => "#52B1E0";

        public List<ICelMixin> CelMixins { get; } = BasicCelType.CreateStandardMixins();
    }
}
