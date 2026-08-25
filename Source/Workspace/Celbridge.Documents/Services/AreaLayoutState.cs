using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// The live layout state of the document areas: per-area split and ratio, which areas the user has visible,
/// and the transient isolation and Utility Rail and Panel presentation. Holds the layout rules over that state;
/// applying it to the visual tree is the section container's job. Mutations return whether the state
/// changed, so the caller knows when to re-apply the layout.
/// </summary>
public class AreaLayoutState
{
    public const double DefaultSplitRatio = 0.5;

    private readonly Dictionary<DocumentArea, bool> _areaSplit = new();
    private readonly Dictionary<DocumentArea, double> _areaSplitRatio = new();
    private readonly HashSet<DocumentArea> _visibleAreas = new();

    private DocumentArea? _isolatedArea;
    private bool _isUtilityPanelPresented = true;
    private bool _isUtilityRailPresented = true;
    private BottomAreaAlignment _bottomAreaAlignment = WorkspaceConstants.BottomAreaAlignment;

    public AreaLayoutState()
    {
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            _areaSplit[area] = false;
            _areaSplitRatio[area] = DefaultSplitRatio;
            _visibleAreas.Add(area);
        }
    }

    /// <summary>
    /// The area currently shown on its own, or null when the areas are laid out normally.
    /// </summary>
    public DocumentArea? IsolatedArea => _isolatedArea;

    /// <summary>
    /// Whether the Utility Panel is showing alongside the document areas.
    /// </summary>
    public bool IsUtilityPanelPresented => _isUtilityPanelPresented;

    /// <summary>
    /// Whether the Utility Rail is showing down the left of the workspace.
    /// </summary>
    public bool IsUtilityRailPresented => _isUtilityRailPresented;

    /// <summary>
    /// Whether anything is showing in the band left of the document areas, which is what an area on that side
    /// draws its left edge against.
    /// </summary>
    public bool IsUtilityColumnPresented => _isUtilityRailPresented || _isUtilityPanelPresented;

    /// <summary>
    /// How far the Bottom area spans across the workspace.
    /// </summary>
    public BottomAreaAlignment BottomAreaAlignment => _bottomAreaAlignment;

    /// <summary>
    /// Whether the Bottom area runs across the Utility Panel's column, so the panel stops above it.
    /// </summary>
    public bool BottomAreaSpansUtilityPanel =>
        IsAreaPresented(DocumentArea.Bottom)
            && (_bottomAreaAlignment == BottomAreaAlignment.Left
                || _bottomAreaAlignment == BottomAreaAlignment.Justify);

    /// <summary>
    /// Whether the Bottom area runs across the Side area's column, so the Side area stops above it.
    /// </summary>
    public bool BottomAreaSpansSideArea =>
        IsAreaPresented(DocumentArea.Bottom)
            && (_bottomAreaAlignment == BottomAreaAlignment.Right
                || _bottomAreaAlignment == BottomAreaAlignment.Justify);

    /// <summary>
    /// Whether the area is currently showing both of its sections.
    /// </summary>
    public bool IsAreaSplit(DocumentArea area)
    {
        return _areaSplit[area];
    }

    /// <summary>
    /// Whether the area is currently visible. Main is always visible.
    /// </summary>
    public bool IsAreaVisible(DocumentArea area)
    {
        return _visibleAreas.Contains(area);
    }

    /// <summary>
    /// Gets the share of a split area taken by its primary section.
    /// </summary>
    public double GetAreaSplitRatio(DocumentArea area)
    {
        return _areaSplitRatio[area];
    }

    /// <summary>
    /// Whether the area takes part in the current layout. While an area is isolated it is the only one
    /// presented; otherwise the collapsible areas follow the visibility the user chose.
    /// </summary>
    public bool IsAreaPresented(DocumentArea area)
    {
        if (_isolatedArea is DocumentArea isolatedArea)
        {
            return isolatedArea == area;
        }

        return _visibleAreas.Contains(area);
    }

    /// <summary>
    /// Whether the section is currently laid out on screen: its area is presented and, for a secondary
    /// section, that area is split.
    /// </summary>
    public bool IsSectionMounted(DocumentSection section)
    {
        return IsAreaPresented(section.GetArea())
            && IsSectionInAreaLayout(section);
    }

    /// <summary>
    /// Every mounted section, in reading order.
    /// </summary>
    public IReadOnlyList<DocumentSection> VisibleSections
    {
        get
        {
            var visible = new List<DocumentSection>();
            foreach (var section in DocumentLayoutHelper.AllSections)
            {
                if (IsSectionMounted(section))
                {
                    visible.Add(section);
                }
            }

            return visible;
        }
    }

    /// <summary>
    /// The sections a fallback active document can be chosen from: those a visible area lays out,
    /// ignoring any isolation. Closing the last document in an isolated area moves to a document
    /// elsewhere rather than reporting that none are left, and the isolation follows it.
    /// </summary>
    public IEnumerable<DocumentSection> SelectableSections
    {
        get
        {
            foreach (var section in DocumentLayoutHelper.AllSections)
            {
                if (_visibleAreas.Contains(section.GetArea()) &&
                    IsSectionInAreaLayout(section))
                {
                    yield return section;
                }
            }
        }
    }

    /// <summary>
    /// Whether a document in the area can be moved into a new split. The area must be unsplit, have room
    /// for two sections, and hold more than one document so the split does not empty its primary section.
    /// </summary>
    public bool CanStartSplit(DocumentArea area, bool hasRoomToSplit, int primaryTabCount)
    {
        if (_areaSplit[area] ||
            !hasRoomToSplit)
        {
            return false;
        }

        return primaryTabCount > 1;
    }

    /// <summary>
    /// Whether a split area must fold back because one of its sections has run out of documents, upholding
    /// the invariant that a split section is never left empty.
    /// </summary>
    public bool ShouldFoldSplit(DocumentArea area, int primaryTabCount, int secondaryTabCount)
    {
        if (!_areaSplit[area])
        {
            return false;
        }

        return primaryTabCount == 0
            || secondaryTabCount == 0;
    }

    public bool SetAreaSplit(DocumentArea area, bool isSplit)
    {
        if (_areaSplit[area] == isSplit)
        {
            return false;
        }

        _areaSplit[area] = isSplit;

        return true;
    }

    /// <summary>
    /// Sets the share of a split area taken by its primary section, rejecting values outside the open
    /// interval between zero and one.
    /// </summary>
    public bool SetAreaSplitRatio(DocumentArea area, double ratio)
    {
        if (double.IsNaN(ratio)
            || double.IsInfinity(ratio)
            || ratio <= 0
            || ratio >= 1)
        {
            return false;
        }

        _areaSplitRatio[area] = ratio;

        return true;
    }

    /// <summary>
    /// Shows or hides an area. Main is always visible, so hiding it is refused.
    /// </summary>
    public bool SetAreaVisible(DocumentArea area, bool isVisible)
    {
        if (!area.IsCollapsible())
        {
            return false;
        }

        if (isVisible)
        {
            return _visibleAreas.Add(area);
        }

        return _visibleAreas.Remove(area);
    }

    public bool SetIsolatedArea(DocumentArea? area)
    {
        if (_isolatedArea == area)
        {
            return false;
        }

        _isolatedArea = area;

        return true;
    }

    public bool SetBottomAreaAlignment(BottomAreaAlignment alignment)
    {
        if (_bottomAreaAlignment == alignment)
        {
            return false;
        }

        _bottomAreaAlignment = alignment;

        return true;
    }

    public bool SetUtilityPanelPresented(bool isPresented)
    {
        if (_isUtilityPanelPresented == isPresented)
        {
            return false;
        }

        _isUtilityPanelPresented = isPresented;

        return true;
    }

    public bool SetUtilityRailPresented(bool isPresented)
    {
        if (_isUtilityRailPresented == isPresented)
        {
            return false;
        }

        _isUtilityRailPresented = isPresented;

        return true;
    }

    // Whether the area's split state lays the section out: a primary section always, a secondary one
    // only while its area is split.
    private bool IsSectionInAreaLayout(DocumentSection section)
    {
        return !section.IsSecondarySection() || _areaSplit[section.GetArea()];
    }
}
