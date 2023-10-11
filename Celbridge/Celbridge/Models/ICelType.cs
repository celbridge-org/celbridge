using System.Collections.Generic;

namespace Celbridge.Models
{
    public interface ICelType
    {
        string Name { get; }
        string Description { get; }
        string Icon { get; }
        string Color { get; }
        List<ICelMixin> CelMixins { get; }
    }
}