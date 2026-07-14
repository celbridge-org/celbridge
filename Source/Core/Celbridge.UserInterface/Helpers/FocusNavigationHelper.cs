using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;
using FocusNavigationDirection = Microsoft.UI.Xaml.Input.FocusNavigationDirection;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Keyboard focus navigation helpers shared by the views and elements that move focus in response to input.
/// </summary>
public static class FocusNavigationHelper
{
    /// <summary>
    /// Moves keyboard focus to the next focusable element in tab order, as pressing Tab would.
    /// </summary>
    public static void MoveFocusToNextElement()
    {
        // Uno obsoletes this overload in favour of the FindNextElementOptions overload, but that overload
        // throws for Next, Previous, and None, so this is the only FocusManager API that can move focus in
        // tab order. Suppress the obsoletion in one place rather than at every call site.
#pragma warning disable CS0618
        FocusManager.TryMoveFocus(FocusNavigationDirection.Next);
#pragma warning restore CS0618
    }
}
