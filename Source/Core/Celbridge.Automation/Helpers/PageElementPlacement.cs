using System.Text.Json;

namespace Celbridge.Automation;

/// <summary>
/// The elements a page found in one of its frames, with their bounds in the window's content. It also carries the
/// frame's name, the match count before the results were capped, and the page's device pixel ratio.
/// </summary>
internal record PageLocation(
    string Frame,
    int TotalMatches,
    double DevicePixelRatio,
    IReadOnlyList<PageElementInfo> Elements);

/// <summary>
/// Converts the element rectangles a page reports in CSS pixels to bounds in the window's content.
/// </summary>
internal static class PageElementPlacement
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Parses the page's locate result and offsets each element by the web view's top left corner. One CSS pixel
    /// is devicePixelRatio / rasterizationScale device-independent pixels.
    /// </summary>
    public static Result<PageLocation> Place(string locateJson, ControlBounds webViewBounds, double rasterizationScale)
    {
        if (rasterizationScale <= 0)
        {
            return Result.Fail($"The window's rasterization scale is {rasterizationScale}, so a page cannot be placed in it.");
        }

        LocateAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<LocateAnswer>(locateJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result.Fail("The page's answer to a locate call could not be read.").WithException(ex);
        }

        if (answer is null)
        {
            return Result.Fail("The page gave no answer to a locate call.");
        }

        var scale = answer.DevicePixelRatio / rasterizationScale;

        var elements = new List<PageElementInfo>();
        foreach (var located in answer.Elements)
        {
            var rect = located.Rect;
            var bounds = new ControlBounds(
                webViewBounds.X + rect.X * scale,
                webViewBounds.Y + rect.Y * scale,
                rect.Width * scale,
                rect.Height * scale);

            var element = new PageElementInfo(
                located.Tag,
                located.Selector,
                located.Role,
                located.AccessibleName,
                located.Visible,
                bounds,
                located.InView,
                located.Text,
                located.Value,
                located.Checked,
                located.Disabled,
                located.Focused);
            elements.Add(element);
        }

        var location = new PageLocation(answer.Frame, answer.TotalMatches, answer.DevicePixelRatio, elements);

        return location;
    }

    // The page's locate result. Each element's rectangle is in CSS pixels, relative to the page's viewport.
    private sealed record LocateAnswer(
        string Frame,
        int TotalMatches,
        double DevicePixelRatio,
        List<LocatedElement> Elements);

    private sealed record LocatedElement(
        string Tag,
        string Selector,
        string Role,
        string AccessibleName,
        bool Visible,
        PageRect Rect,
        bool InView,
        string Text,
        string? Value,
        bool? Checked,
        bool Disabled,
        bool Focused);

    private sealed record PageRect(double X, double Y, double Width, double Height);
}
