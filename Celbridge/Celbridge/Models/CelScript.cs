using Celbridge.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace Celbridge.Models
{
    public interface ICelScript
    {
        IEntity? Entity { get; set; }
        ObservableCollection<ICelScriptNode> Cels { get; }
        Result AddCel(ICelScriptNode cel);
        Result DeleteCel(ICelScriptNode cel);
    }

    public partial class CelScript : ObservableObject, ICelScript, ITreeNode
    {
        public ITreeNodeRef ParentNode { get; } = new ParentNodeRef();
        public void OnSetParent(ITreeNode parent)
        {
            // Set this CelScript as the parent of the Cels
            foreach (var cel in Cels)
            {
                if (cel is ITreeNode childNode)
                {
                    ParentNodeRef.SetParent(childNode, this);
                }
            }
        }

        [JsonIgnore]
        public IEntity? Entity { get; set; }

        public ObservableCollection<ICelScriptNode> Cels { get; set; } = new();

        public Result AddCel(ICelScriptNode cel)
        {
            if (cel is null)
            {
                return new ErrorResult("Cel cannot be null");
            }
            if (Cels.Contains(cel))
            {
                return new ErrorResult($"Cel '{cel.Name}' already added to module");
            }

            if (cel is ITreeNode childNode)
            {
                // Set this CelScript as the parent of the new Cel
                ParentNodeRef.SetParent(childNode, this);
            }

            Cels.Add(cel);
            return new SuccessResult();
        }

        public Result DeleteCel(ICelScriptNode cel)
        {
            if (cel is null)
            {
                return new ErrorResult("Cel cannot be null");
            }
            if (!Cels.Contains(cel))
            {
                return new ErrorResult($"Cel '{cel.Name}' does not exist in module.");
            }
            Cels.Remove(cel);
            return new SuccessResult();
        }
    }
}
