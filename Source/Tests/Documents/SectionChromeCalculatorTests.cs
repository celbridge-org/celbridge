using Celbridge.Documents;
using Celbridge.Documents.Helpers;
using Celbridge.Documents.Services;
using Microsoft.UI.Xaml;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers SectionChromeCalculator: which edges a section draws for the presented-area combinations,
/// which corners round (only where both meeting edges face a gutter outside the area), the square
/// internal cut of a split area, and the bottom-corner gate for heads that cannot clip a web view.
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

    private SectionChromeCalculator CreateCalculator(bool roundsBottomCorners = false)
    {
        return new SectionChromeCalculator(_layoutState, roundsBottomCorners);
    }

    [Test]
    public void DefaultLayout_MainDrawsEveryEdgeAndRoundsItsTopCorners()
    {
        var calculator = CreateCalculator();

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        // Left faces the Utility Panel, top the title bar, right the Side area, bottom the Bottom area.
        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 1, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, Radius, 0, 0));
        areaChrome.Secondary.Should().BeNull();
    }

    [Test]
    public void DefaultLayout_RoundingBottomCornersRoundsAllFourOnMain()
    {
        var calculator = CreateCalculator(roundsBottomCorners: true);

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Main, Radius);

        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, Radius, Radius, Radius));
    }

    [Test]
    public void DefaultLayout_SideLeavesItsWindowEdgesBare()
    {
        var calculator = CreateCalculator(roundsBottomCorners: true);

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        // Right and bottom sit on the application border, so they draw nothing and their corners
        // stay square even on a head that rounds bottom corners.
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
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(0, Radius, 0, 0));
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
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, 0));

        areaChrome.Secondary.Should().NotBeNull();
        areaChrome.Secondary!.Edges.Should().Be(new Thickness(1, 1, 1, 1));
        areaChrome.Secondary.Corners.Should().Be(new CornerRadius(0, Radius, 0, 0));
    }

    [Test]
    public void SplitSideArea_DividesVerticallyWithTheSameSquareCut()
    {
        _layoutState.SetAreaSplit(DocumentArea.Side, true);
        var calculator = CreateCalculator(roundsBottomCorners: true);

        var areaChrome = calculator.CalculateAreaChrome(DocumentArea.Side, Radius);

        areaChrome.Primary.Edges.Should().Be(new Thickness(1, 1, 0, 1));
        areaChrome.Primary.Corners.Should().Be(new CornerRadius(Radius, 0, 0, 0));

        areaChrome.Secondary.Should().NotBeNull();
        areaChrome.Secondary!.Edges.Should().Be(new Thickness(1, 1, 0, 0));

        // The secondary section's top edge is the internal cut, so nothing rounds: its left edge is its
        // only outer edge with a partner, and both corners on the window side stay square.
        areaChrome.Secondary.Corners.Should().Be(new CornerRadius(0, 0, 0, 0));
    }
}
