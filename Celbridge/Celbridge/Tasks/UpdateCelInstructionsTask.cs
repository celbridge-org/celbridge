using Celbridge.Models;
using Celbridge.Models.CelMixins;
using Celbridge.Utils;

namespace Celbridge.Tasks
{
    public class UpdateCelInstructionsTask
    {
        public Result Update(ICel cel)
        {
            var instructionLines = cel.Instructions;

            // Reset the meta data for all instructions
            // Todo: Reset and update the instructions in a single pass.
            for (int i = 0; i < instructionLines.Count; i++)
            {
                var instructionLine = instructionLines[i];
                var instruction = instructionLine.Instruction;
                if (instruction == null || instruction.GetType() == typeof(EmptyInstruction))
                {
                    continue;
                }
                instruction.PipeState = PipeState.NotConnected;
            }

            // Update the meta data for each instruction line
            for (int i = 0; i < instructionLines.Count; i++)
            {
                var instructionLine = instructionLines[i];

                var prevInstructionLine = (i > 0) ? instructionLines[i - 1] : null;
                var nextInstructionLine = (i < instructionLines.Count - 1) ? instructionLines[i + 1] : null;

                var instruction = instructionLine.Instruction;
                if (instruction == null || instruction.GetType() == typeof(EmptyInstruction))
                {
                    continue;
                }

                var prevInstruction = prevInstructionLine != null ? prevInstructionLine.Instruction : null;
                var nextInstruction = nextInstructionLine != null ? nextInstructionLine.Instruction : null;

                if (instruction is ITypeInstruction typeInstruction)
                {
                    var consumerTypeName = typeInstruction.GetType().Name;
                    string producerTypeName = string.Empty;

                    if (nextInstruction is BasicMixin.Call callInstruction)
                    {
                        // Todo: We don't need to update this on every keypress, can use a timer as long we don't build the code until it updates
                        // Todo: Can we use a c# type instead of a string for the return type? This seems a bit flaky.
                        producerTypeName = callInstruction.Arguments.CelSignature.ReturnType;
                    }
                    else if (nextInstruction is FileMixin.Read readInstruction)
                    {
                        // Todo: This is very hard coded, replace with a generic mechanism that works for any instruction
                        // A PipeProducerType property would do the trick
                        producerTypeName = nameof(PrimitivesMixin.String);
                    }
                    else if (nextInstruction is ChatMixin.Ask askInstruction)
                    {
                        producerTypeName = nameof(PrimitivesMixin.String);
                    }
                    else if (nextInstruction is BasicMixin.StartProcess startProcess)
                    {
                        producerTypeName = nameof(PrimitivesMixin.String);
                    }

                    // Check if the type of the consuming instruction matches the type of the producing instruction
                    bool canConsumeType = consumerTypeName == producerTypeName;
                    if (!canConsumeType)
                    {
                        // The Set instruction is a special case, it can consume any type
                        canConsumeType = consumerTypeName == nameof(BasicMixin.Set) && !string.IsNullOrEmpty(producerTypeName);
                    }

                    if (canConsumeType)
                    {
                        instruction.PipeState = PipeState.PipeConsumer;
                        nextInstruction!.PipeState = PipeState.PipeProducer;
                    }
                }
            }

            return new SuccessResult();
        }
    }
}
