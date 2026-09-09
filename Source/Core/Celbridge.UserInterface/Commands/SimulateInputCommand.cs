using Celbridge.Commands;
using Celbridge.UserInterface.Platform;

namespace Celbridge.UserInterface.Commands;

public class SimulateInputCommand : CommandBase, ISimulateInputCommand
{
    public string Key { get; set; } = string.Empty;
    public string Modifiers { get; set; } = string.Empty;

    public override Task<Result> ExecuteAsync()
    {
        var command = false;
        var control = false;
        var shift = false;
        var option = false;

        foreach (var token in Modifiers.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "command":
                    command = true;
                    break;
                case "control":
                    control = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "option":
                    option = true;
                    break;
                default:
                    return Task.FromResult<Result>(Result.Fail(
                        $"Unknown modifier '{token}'. Supported modifiers: command, control, shift, option."));
            }
        }

        return Task.FromResult(MacOSInputSimulator.PressKey(Key, command, control, shift, option));
    }
}
