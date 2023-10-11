using Celbridge.Models.CelMixins;
using System.Collections.Generic;

namespace Celbridge.Models.CelTypes
{
    public class BasicCelType : ICelType
    {
        public string Name => "Basic";
        public string Description => "General purpose instructions";
        public string Icon => "PostUpdate";
        public string Color => "#366854";

        public List<ICelMixin> CelMixins { get; } = new()
        {
            new PrimitivesMixin(),
            new BasicMixin()
        };
    }
}
