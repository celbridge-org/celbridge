using CommunityToolkit.Diagnostics;
using System;
using System.IO;

namespace Celbridge.Models
{
    public interface ICallArguments
    {
        public Guid CelId { get; set; }
        public string CelScriptName { get; set; }
        public string CelName { get; set;  }
        public ICelSignature CelSignature { get; set; }
        public string GetSummary(PropertyContext context);
    }

    public record CallArguments : ICallArguments, IRecord, ITreeNode
    {
        public ITreeNodeRef ParentNode { get; } = new ParentNodeRef();
        public void OnSetParent(ITreeNode parent)
        {}

        public Guid CelId { get; set; }

        [HideProperty]
        public string CelScriptName { get; set; }

        [HideProperty]
        public string CelName { get; set; }
        public ICelSignature CelSignature { get; set; } = new CelSignature();

        public string GetSummary(PropertyContext context)
        {
            var signatureSummary = CelSignature.GetSummary(context);

            var parentCelScript = ParentNodeRef.FindParent<ICelScript>(ParentNode.TreeNode) as ICelScript;
            Guard.IsNotNull(parentCelScript);

            var parentCelScriptName = parentCelScript.Entity.Name;
            parentCelScriptName = Path.GetFileNameWithoutExtension(parentCelScriptName);

            if (string.IsNullOrEmpty(CelScriptName) ||
                parentCelScriptName == CelScriptName)
            {
                return $"{CelName} {signatureSummary}";
            }

            return $"{CelScriptName}.{CelName} {signatureSummary}";
        }
    }
}
