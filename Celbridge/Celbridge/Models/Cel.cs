using Celbridge.Models.CelMixins;
using Celbridge.Utils;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;

namespace Celbridge.Models
{
    public interface ICel
    {
        string Name { get; }
        string CelTypeName { get; }
        ICelType CelType { get; }
        List<InstructionLine> Input { get; set; }
        List<InstructionLine> Output { get; set; }
        List<InstructionLine> Instructions { get; set; }
        List<Guid> ConnectedCelIds { get; }
    }

    record CelSignatureChangedMessage(ICel cel);

    public class Cel : CelScriptNode, ICel
    {
        // The CelTypeName is serialized with the Cel, but the CelType is not because CelTypes
        // are managed by the CelTypeService. The CelType reference is populated after deserialization
        // by looking it up in the CelTypeService.
        [HideProperty]
        public string CelTypeName { get; set; }

        [JsonIgnore]
        public ICelType CelType { get; set; }

        [PropertyContext(PropertyContext.CelInput)]
        public List<InstructionLine> Input { get; set; } = new ();

        [MaxListLength(1)]
        [PropertyContext(PropertyContext.CelOutput)]
        public List<InstructionLine> Output { get; set; } = new();

        [PropertyContext(PropertyContext.CelInstructions)]
        public List<InstructionLine> Instructions { get; set; } = new ();

        public override void OnSetParent(ITreeNode parent)
        {
            // Set this Cel as the parent of all the InstructionLines

            foreach (var line in Input)
            {
                if (line is ITreeNode childNode)
                {
                    ParentNodeRef.SetParent(childNode, this);
                }
            }

            foreach (var line in Output)
            {
                if (line is ITreeNode childNode)
                {
                    ParentNodeRef.SetParent(childNode, this);
                }
            }

            foreach (var line in Instructions)
            {
                if (line is ITreeNode childNode)
                {
                    ParentNodeRef.SetParent(childNode, this);
                }
            }
        }

        public List<Guid> ConnectedCelIds
        {
            get
            {
                List<Guid> connectedCels = new ();
                foreach (var instructionLine in Instructions)
                {
                    var instruction = instructionLine.Instruction;
                    if (instruction is BasicMixin.Call call)
                    {
                        var connectedCelId = call.Arguments.CelId;
                        if (connectedCelId != Guid.Empty && !connectedCels.Contains(connectedCelId))
                        {
                            connectedCels.Add(connectedCelId);
                        }
                    }
                }
                return connectedCels;
            }
        }
    }
}