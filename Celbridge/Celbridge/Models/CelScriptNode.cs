using Newtonsoft.Json;
using System;

namespace Celbridge.Models
{
    public interface ICelScriptNode
    {
        ICelScript? CelScript { get; set; }
        string Name { get; set; }
        Guid Id { get; set; }
        string Description { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public record CelConnectionsChangedMessage();

    // Represents a single node in a CelScript
    public abstract class CelScriptNode : Entity, ICelScriptNode
    {
        [JsonIgnore]
        public ICelScript? CelScript { get; set; }

        private int _x;
        public int X
        {
            get => _x;
            set
            {
                SetProperty(ref _x, value);
            }
        }

        private int _y;
        public int Y
        {
            get => _y;
            set
            {
                SetProperty(ref _y, value);
            }
        }
    }
}