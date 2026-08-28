using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers the rule that decides which document area a workspace item opens in when the caller names none.
/// </summary>
[TestFixture]
public class WorkspaceAreaHelperTests
{
    [Test]
    public void TryGetDocumentArea_DefaultIsADocumentArea_ReturnsIt()
    {
        var allowedAreas = new List<WorkspaceArea>
        {
            WorkspaceArea.Utility,
            WorkspaceArea.Main,
            WorkspaceArea.Bottom
        };

        var resolved = WorkspaceAreaHelper.TryGetDocumentArea(allowedAreas, WorkspaceArea.Bottom, out var documentArea);

        resolved.Should().BeTrue();
        documentArea.Should().Be(WorkspaceArea.Bottom);
    }

    [Test]
    public void TryGetDocumentArea_DefaultIsTheUtilityPanel_ReturnsTheOneDocumentArea()
    {
        var allowedAreas = new List<WorkspaceArea>
        {
            WorkspaceArea.Utility,
            WorkspaceArea.Side
        };

        var resolved = WorkspaceAreaHelper.TryGetDocumentArea(allowedAreas, WorkspaceArea.Utility, out var documentArea);

        resolved.Should().BeTrue();
        documentArea.Should().Be(WorkspaceArea.Side);
    }

    [Test]
    public void TryGetDocumentArea_SeveralDocumentAreasAndNoDocumentDefault_ResolvesNothing()
    {
        // Nothing in the declaration picks between them, so the caller has to name the area it wants.
        var allowedAreas = new List<WorkspaceArea>
        {
            WorkspaceArea.Utility,
            WorkspaceArea.Main,
            WorkspaceArea.Bottom
        };

        var resolved = WorkspaceAreaHelper.TryGetDocumentArea(allowedAreas, WorkspaceArea.Utility, out _);

        resolved.Should().BeFalse();
    }

    [Test]
    public void TryGetDocumentArea_PanelOnlyItem_ResolvesNothing()
    {
        var allowedAreas = new List<WorkspaceArea>
        {
            WorkspaceArea.Utility
        };

        var resolved = WorkspaceAreaHelper.TryGetDocumentArea(allowedAreas, WorkspaceArea.Utility, out _);

        resolved.Should().BeFalse();
    }
}
