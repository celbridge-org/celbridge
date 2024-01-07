using CommunityToolkit.Diagnostics;
using System;
using Uno.Extensions;
using Newtonsoft.Json;

namespace Celbridge.Models
{
    public interface ITreeNode
    {
        [JsonIgnore]
        ITreeNodeRef ParentNode { get; }
        void OnSetParent(ITreeNode parent);
    }

    public interface ITreeNodeRef
    {
        ITreeNode? TreeNode { get; }
    }

    public class ParentNodeRef : ITreeNodeRef
    {
        private WeakReference<ITreeNode>? _parent;
        public ITreeNode? TreeNode => _parent?.GetTarget();

        // Create a parent child relationsip between two objects that implement ITreeNode
        public static void SetParent(ITreeNode child, ITreeNode parent)
        {
            Guard.IsNotNull(child);
            Guard.IsNotNull(parent);

            var node = child.ParentNode as ParentNodeRef;
            Guard.IsNotNull(node);

            node._parent = new WeakReference<ITreeNode>(parent);
            child.OnSetParent(parent);

            // Serilog.Log.Information($"'{parent.GetType().Name}' is the parent of '{child.GetType().Name}'");
        }

        public static ITreeNode? FindParent<T>(ITreeNode treeNode)
        {
            if (treeNode.ParentNode == null ||
                treeNode.ParentNode.TreeNode == null)
            {
                return null;
            }

            var parentNode = treeNode.ParentNode.TreeNode;
            if (parentNode.GetType().IsAssignableTo(typeof(T)))
            {
                return parentNode;
            }

            return FindParent<T>(parentNode);
        }
    }
}
