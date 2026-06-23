using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Commands;

public class SpotlightCommand : CommandBase, ISpotlightCommand
{
    private readonly IMessengerService _messengerService;
    private readonly ILayoutService _layoutService;

    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DurationMs { get; set; }

    public SpotlightCommand(
        IMessengerService messengerService,
        ILayoutService layoutService)
    {
        _messengerService = messengerService;
        _layoutService = layoutService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        // An empty target clears any visible spotlight.
        if (string.IsNullOrEmpty(Target))
        {
            _messengerService.Send(new ClearSpotlightMessage());
            return Result.Ok();
        }

        // Reveal the landmark's region before showing the tip, so spotlighting a
        // landmark in a collapsed panel opens that panel first. Unknown targets
        // are rejected at the tool, so a missing descriptor here just skips the
        // reveal and lets the view's resolution report the miss.
        var descriptor = LandmarkCatalog.All.FirstOrDefault(landmark => landmark.Id == Target);
        if (descriptor is not null &&
            descriptor.Region is not null)
        {
            _layoutService.SetRegionVisibility(descriptor.Region.Value, true);
        }

        var message = new ShowSpotlightMessage(Target, Label, DurationMs);
        _messengerService.Send(message);

        return Result.Ok();
    }
}
