using Celbridge.Commands;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.Commands;

public class SpotlightCommand : CommandBase, ISpotlightCommand
{
    private readonly ISpotlightService _spotlightService;

    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DurationMs { get; set; }

    public SpotlightCommand(ISpotlightService spotlightService)
    {
        _spotlightService = spotlightService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // An empty target clears any visible spotlight.
        if (string.IsNullOrEmpty(Target))
        {
            _spotlightService.ClearSpotlight();
            return Result.Ok();
        }

        // The show reveals the target and reports a descriptive failure when it cannot be made
        // visible, which the tool surfaces to the agent.
        return await _spotlightService.ShowSpotlightAsync(Target, Label, DurationMs);
    }
}
