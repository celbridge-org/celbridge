using Newtonsoft.Json;

namespace Celbridge.Models
{
    public interface IRecord : IEditable
    {
        [JsonIgnore]
        public string Description => ToString() ?? string.Empty;
    }
}
