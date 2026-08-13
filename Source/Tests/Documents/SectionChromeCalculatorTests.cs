using Celbridge.Documents;
using Celbridge.Documents.Helpers;
using Celbridge.Documents.Services;
using Celbridge.Workspace;
using Microsoft.UI.Xaml;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers SectionChromeCalculator: which edges a section draws for the presented-area combinations,
/// which corners round (only where both meeting edges face a gutter outside the area), and the square
/// internal cut of a split area.
/// </summary>
[TestFixture]
public class SectionChromeCalculatorTests
{
    private const double Radius = 8;

    private AreaLayoutState _layoutState = null!;

    [SetUp]
    public void Setup()
    {
        _layoutState = new AreaLayoutState();
    }

    private SectionChromeCalculator CreateCalculator()
    {
        return new SectionChromeCalculator(_layoutState);
    }

    [Test]
    public void DefaultLayout_MainDrawsEveryEdgeAndRoundsEveryCorner()
    {
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        // Left faces the Utility Panel, top the title bar, right the Side area, bottom the Bottom area.
        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 1, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, Radius, Radius, Radius));
        areaChrome.Secondary.Should().BeNull();
    }

    [Test]
    public void DefaultLayout_SideLeavesItsWindowEdgesBare()
    {
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        // Right and bottom sit on the application border, so they draw nothing and the corners they
        // meet at stay square.
        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 0));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, 0));
    }

    [Test]
    public void HiddenUtilityPanel_MainLeavesItsLeftEdgeBare()
    {
        _layoutState.SetUtilityPanelPresented(false);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        areaChrome.Primary.Edges.Should().Be(new Thickness(0, 1, 1, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(0, Radius, Radius, 0));
    }

    [Test]
    public void HiddenBottomAndSideAreas_MainDrawsOnlyTheChromeFacingEdges()
    {
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false);
        _layoutState.SetAreaVisible(DocumentArea.Side, false);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 0));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, 0));
    }

    [Test]
    public void IsolatedSideArea_FacesOnlyTheUtilityPanelAndTitleBar()
    {
        _layoutState.SetIsolatedArea(DocumentArea.Side);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        // The main column is not presented, so the left edge falls back to facing the Utility Panel.
        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 0));
    }

    [Test]
    public void SplitMainArea_SectionsShareOnePerimeterWithASquareCut()
    {
        _layoutState.SetAreaSplit(DocumentArea.Main, true);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        // Each section draws an inner edge against the split gutter, but that edge shapes no corners:
        // the pair rounds only the outer perimeter, marking them as one area.
        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 1, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, Radius));

        areaChrome.Secondary.Should().NotBeNull();
        areaChrome.Secondary!.Edges.Should().Be(new Thickness(1, 1, 1, 1));
        areaChrome.Secondary.Corners.Should().Be(new CornerRadius(0, Radius, Radius, 0));
    }

    [Test]
    public void SplitSideArea_DividesVerticallyWithTheSameSquareCut()
    {
        _layoutState.SetAreaSplit(DocumentArea.Side, true);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, 0));

        areaChrome.Secondary.Should().NotBeNull();
        areaChrome.Secondary!.Edges.Should().Be(new Thickness(1, 1, 0, 0));

        // The secondary section's top edge is the internal cut, so nothing rounds: its left edge is its
        // only outer edge with a partner, and both corners on the window side stay square.
        areaChrome.Secondary.Corners.Should().Be(new CornerRadius(0, 0, 0, 0));
    }

    [Test]
    public void LeftAlignedBottomArea_BottomLeavesItsLeftEdgeBare()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Left);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Bottom, Radius);

        // The Bottom area now runs under the Utility Panel to the application border, so it no longer
        // faces the panel across a gutter. Its right edge still faces the Side area.
        areaChrome.Primary.Edges.Should().Be(new Thickness(0, 1, 1, 0));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(0, Radius, 0, 0));
    }

    [Test]
    public void RightAlignedBottomArea_SideGainsABottomEdge()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Right);
        var calculator = CreateCalculator();

        var bottomChrome = calculator.CalculateAreaChrome(DocumentArea.Bottom, Radius);
        var sideChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        // The Bottom area reaches the border on the right, and the Side area it now runs under stops
        // above it with a gutter to draw against.
        bottomChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 0));
        sideChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 1));
    }

    [Test]
    public void JustifiedBottomArea_BottomLeavesBothSideEdgesBare()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Justify);
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Bottom, Radius);

        // Only the top edge faces a gutter, so no corner has two edges to round between.
        areaChrome.Primary.Edges.Should().Be(new Thickness(0, 1, 0, 0));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(0, 0, 0, 0));
    }

    [Test]
    public void HiddenBottomArea_AlignmentLeavesTheOtherAreasUnchanged()
    {
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false);
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Justify);
        var calculator = CreateCalculator();

        var sideChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);
        var mainChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        // An area that is not presented spans nothing, so the Side area keeps its full height and Main
        // has no Bottom area to face.
        sideChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 0));
        mainChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 1, 0));
    }
}
