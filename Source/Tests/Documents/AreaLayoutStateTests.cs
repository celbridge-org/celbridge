using Celbridge.Documents;
using Celbridge.Documents.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers AreaLayoutState: the presentation rules (isolation overriding visibility, secondary sections
/// mounting only while their area is split), the fallback-selection rule that ignores isolation, the
/// split-start and fold invariants, and split-ratio validation.
/// </summary>
[TestFixture]
public class AreaLayoutStateTests
{
    private AreaLayoutState _layoutState = null!;

    [SetUp]
    public void Setup()
    {
        _layoutState = new AreaLayoutState();
    }

    [Test]
    public void InitialState_AllAreasVisibleAndUnsplit()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _layoutState.IsAreaVisible(area).Should().BeTrue();
            _layoutState.IsAreaSplit(area).Should().BeFalse();
            _layoutState.GetAreaSplitRatio(area).Should().Be(AreaLayoutState.DefaultSplitRatio);
        }

        _layoutState.IsolatedArea.Should().BeNull();
        _layoutState.IsUtilityPanelPresented.Should().BeTrue();
    }

    [Test]
    public void VisibleSections_DefaultLayout_ListsThePrimarySections()
    {
        _layoutState.VisibleSections.Should().Equal(
            DocumentSection.MainLeft,
            DocumentSection.BottomLeft,
            DocumentSection.SideTop);
    }

    [Test]
    public void IsSectionMounted_SecondarySection_RequiresItsAreaToBeSplit()
    {
        _layoutState.IsSectionMounted(DocumentSection.MainRight).Should().BeFalse();

        _layoutState.SetAreaSplit(DocumentArea.Main, true);

        _layoutState.IsSectionMounted(DocumentSection.MainRight).Should().BeTrue();
    }

    [Test]
    public void IsSectionMounted_HiddenArea_UnmountsBothSections()
    {
        _layoutState.SetAreaSplit(DocumentArea.Side, true);
        _layoutState.SetAreaVisible(DocumentArea.Side, false);

        _layoutState.IsSectionMounted(DocumentSection.SideTop).Should().BeFalse();
        _layoutState.IsSectionMounted(DocumentSection.SideBottom).Should().BeFalse();
    }

    [Test]
    public void IsAreaPresented_IsolatedArea_IsTheOnlyOnePresented()
    {
        _layoutState.SetIsolatedArea(DocumentArea.Side);

        _layoutState.IsAreaPresented(DocumentArea.Side).Should().BeTrue();
        _layoutState.IsAreaPresented(DocumentArea.Main).Should().BeFalse();
        _layoutState.IsAreaPresented(DocumentArea.Bottom).Should().BeFalse();
    }

    [Test]
    public void IsAreaPresented_IsolationOverridesHiddenVisibility()
    {
        // Isolation presents the area even when the user had it hidden, and hides the rest without
        // touching their stored visibility.
        _layoutState.SetAreaVisible(DocumentArea.Side, false);
        _layoutState.SetIsolatedArea(DocumentArea.Side);

        _layoutState.IsAreaPresented(DocumentArea.Side).Should().BeTrue();
        _layoutState.IsAreaVisible(DocumentArea.Side).Should().BeFalse();
    }

    [Test]
    public void IsAreaPresented_ClearingIsolation_RestoresTheStoredVisibility()
    {
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false);
        _layoutState.SetIsolatedArea(DocumentArea.Main);
        _layoutState.SetIsolatedArea(null);

        _layoutState.IsAreaPresented(DocumentArea.Main).Should().BeTrue();
        _layoutState.IsAreaPresented(DocumentArea.Bottom).Should().BeFalse();
        _layoutState.IsAreaPresented(DocumentArea.Side).Should().BeTrue();
    }

    [Test]
    public void SelectableSections_IgnoresIsolationButRespectsVisibilityAndSplit()
    {
        // Closing the last document in an isolated area must find a fallback elsewhere, so sections
        // outside the isolated area stay selectable while hidden or unsplit ones do not.
        _layoutState.SetAreaSplit(DocumentArea.Main, true);
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false);
        _layoutState.SetIsolatedArea(DocumentArea.Side);

        _layoutState.SelectableSections.Should().Equal(
            DocumentSection.MainLeft,
            DocumentSection.MainRight,
            DocumentSection.SideTop);
    }

    [Test]
    public void SetAreaVisible_MainArea_IsRefused()
    {
        _layoutState.SetAreaVisible(DocumentArea.Main, false).Should().BeFalse();

        _layoutState.IsAreaVisible(DocumentArea.Main).Should().BeTrue();
    }

    [Test]
    public void SetAreaVisible_ReportsWhetherTheStateChanged()
    {
        _layoutState.SetAreaVisible(DocumentArea.Bottom, true).Should().BeFalse();
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false).Should().BeTrue();
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false).Should().BeFalse();
    }

    [Test]
    public void SetAreaSplit_ReportsWhetherTheStateChanged()
    {
        _layoutState.SetAreaSplit(DocumentArea.Main, false).Should().BeFalse();
        _layoutState.SetAreaSplit(DocumentArea.Main, true).Should().BeTrue();
        _layoutState.SetAreaSplit(DocumentArea.Main, true).Should().BeFalse();
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(-0.5)]
    [TestCase(1.5)]
    public void SetAreaSplitRatio_InvalidRatio_IsRejected(double ratio)
    {
        _layoutState.SetAreaSplitRatio(DocumentArea.Main, ratio).Should().BeFalse();

        _layoutState.GetAreaSplitRatio(DocumentArea.Main).Should().Be(AreaLayoutState.DefaultSplitRatio);
    }

    [Test]
    public void SetAreaSplitRatio_ValidRatio_IsStored()
    {
        _layoutState.SetAreaSplitRatio(DocumentArea.Main, 0.3).Should().BeTrue();

        _layoutState.GetAreaSplitRatio(DocumentArea.Main).Should().Be(0.3);
    }

    [Test]
    public void CanStartSplit_RequiresRoomAndADocumentToLeaveBehind()
    {
        _layoutState.CanStartSplit(DocumentArea.Main, hasRoomToSplit: true, primaryTabCount: 2).Should().BeTrue();
        _layoutState.CanStartSplit(DocumentArea.Main, hasRoomToSplit: false, primaryTabCount: 2).Should().BeFalse();
        _layoutState.CanStartSplit(DocumentArea.Main, hasRoomToSplit: true, primaryTabCount: 1).Should().BeFalse();
    }

    [Test]
    public void CanStartSplit_AlreadySplitArea_IsRefused()
    {
        _layoutState.SetAreaSplit(DocumentArea.Main, true);

        _layoutState.CanStartSplit(DocumentArea.Main, hasRoomToSplit: true, primaryTabCount: 2).Should().BeFalse();
    }

    [Test]
    public void ShouldFoldSplit_SplitArea_FoldsWhenEitherSectionIsEmpty()
    {
        _layoutState.SetAreaSplit(DocumentArea.Main, true);

        _layoutState.ShouldFoldSplit(DocumentArea.Main, primaryTabCount: 1, secondaryTabCount: 1).Should().BeFalse();
        _layoutState.ShouldFoldSplit(DocumentArea.Main, primaryTabCount: 0, secondaryTabCount: 1).Should().BeTrue();
        _layoutState.ShouldFoldSplit(DocumentArea.Main, primaryTabCount: 1, secondaryTabCount: 0).Should().BeTrue();
    }

    [Test]
    public void ShouldFoldSplit_UnsplitArea_NeverFolds()
    {
        _layoutState.ShouldFoldSplit(DocumentArea.Main, primaryTabCount: 0, secondaryTabCount: 0).Should().BeFalse();
    }

    [Test]
    public void InitialState_BottomAreaAlignmentIsCenterAndSpansNeitherNeighbour()
    {
        _layoutState.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Center);
        _layoutState.BottomAreaSpansUtilityPanel.Should().BeFalse();
        _layoutState.BottomAreaSpansSideArea.Should().BeFalse();
    }

    [Test]
    public void BottomAreaAlignment_EachModeSpansItsOwnNeighbours()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Left);
        _layoutState.BottomAreaSpansUtilityPanel.Should().BeTrue();
        _layoutState.BottomAreaSpansSideArea.Should().BeFalse();

        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Right);
        _layoutState.BottomAreaSpansUtilityPanel.Should().BeFalse();
        _layoutState.BottomAreaSpansSideArea.Should().BeTrue();

        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Justify);
        _layoutState.BottomAreaSpansUtilityPanel.Should().BeTrue();
        _layoutState.BottomAreaSpansSideArea.Should().BeTrue();
    }

    [Test]
    public void BottomAreaAlignment_HiddenBottomArea_SpansNothing()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Justify);
        _layoutState.SetAreaVisible(DocumentArea.Bottom, false);

        _layoutState.BottomAreaSpansUtilityPanel.Should().BeFalse();
        _layoutState.BottomAreaSpansSideArea.Should().BeFalse();
    }

    [Test]
    public void BottomAreaAlignment_IsolatedArea_SpansNothing()
    {
        // Focus and Presentation isolate one area, so no other area is laid out to be spanned.
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Justify);
        _layoutState.SetIsolatedArea(DocumentArea.Main);

        _layoutState.BottomAreaSpansUtilityPanel.Should().BeFalse();
        _layoutState.BottomAreaSpansSideArea.Should().BeFalse();
    }

    [Test]
    public void SetBottomAreaAlignment_SameAlignment_ReportsNoChange()
    {
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Left).Should().BeTrue();
        _layoutState.SetBottomAreaAlignment(BottomAreaAlignment.Left).Should().BeFalse();
    }
}
