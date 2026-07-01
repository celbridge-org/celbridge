using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.Views;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Hosts the application toolbar directly on the Skia desktop heads, which draw a native title bar above
/// it.
/// </summary>
internal sealed class SkiaApplicationToolbarHost : IApplicationToolbarHost
{
    public ITitleBar Install(Window window, Panel layoutRoot)
    {
        var applicationToolbar = new ApplicationToolbar();
        layoutRoot.Children.Add(applicationToolbar);

        return applicationToolbar;
    }
}
