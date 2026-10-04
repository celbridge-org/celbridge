using Celbridge.Automation;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for converting the element rectangles a page reports in CSS pixels to bounds in the window's content.
/// </summary>
[TestFixture]
public class PageElementPlacementTests
{
    private const string LocateAnswer = """
        {
          "frame": "#preview",
          "totalMatches": 3,
          "devicePixelRatio": 2.5,
          "elements": [
            {
              "tag": "input",
              "selector": "#name",
              "role": "textbox",
              "accessibleName": "Name",
              "visible": true,
              "rect": {"x": 10, "y": 20, "width": 200, "height": 24},
              "inView": true,
              "text": "",
              "value": "Ada",
              "checked": null,
              "disabled": false,
              "focused": true
            }
          ]
        }
        """;

    private static readonly ControlBounds WebViewBounds = new(300, 100, 800, 600);

    [Test]
    public void Place_ScalesByThePixelRatioOverTheRasterizationScale_FromTheWebViewCorner()
    {
        var result = PageElementPlacement.Place(LocateAnswer, WebViewBounds, 2);

        result.IsSuccess.Should().BeTrue();
        var location = result.Value;
        location.Frame.Should().Be("#preview");
        location.TotalMatches.Should().Be(3);
        location.DevicePixelRatio.Should().Be(2.5);

        var element = location.Elements.Single();
        element.Bounds.Should().Be(new ControlBounds(312.5, 125, 250, 30));
        element.Selector.Should().Be("#name");
        element.Value.Should().Be("Ada");
        element.IsChecked.Should().BeNull();
        element.IsInView.Should().BeTrue();
        element.IsFocused.Should().BeTrue();
    }

    [Test]
    public void Place_AnswerThatDoesNotParse_Fails()
    {
        var result = PageElementPlacement.Place("{not json", WebViewBounds, 2);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Place_NoRasterizationScale_Fails()
    {
        var result = PageElementPlacement.Place(LocateAnswer, WebViewBounds, 0);

        result.IsFailure.Should().BeTrue();
    }
}
