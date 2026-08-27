using System.Collections;
using System.Collections.Specialized;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Celbridge.UserInterface.Views.Controls;

// One card in the list. Container is what the list positions and drags; Card is the expander inside it, and
// Outline is the ring drawn over the expander's own border to mark the card the pointer is carrying. The
// transform holds the card under the pointer, and the grip glyph takes the accent with the outline. A
// single-card list carries no grip, so that one is optional.
internal sealed record CardEntry(
    object Item,
    Grid Container,
    Expander Card,
    Border Outline,
    TranslateTransform Transform,
    Icon? GripIcon);

// What a reorder drag carries from the grab to the drop. The pitch and grab offset are measured once, at
// the grab, and never re-read from the rows.
internal sealed class CardDragState
{
    public required CardEntry Entry { get; init; }

    public required Pointer Pointer { get; init; }

    public required int OriginalIndex { get; init; }

    public required double GrabOffset { get; init; }

    public required double RowPitch { get; init; }

    public int SlotIndex { get; set; }
}

/// <summary>
/// Editor for a setting that is a list of records: one expander card per entry, with add, delete and
/// drag-to-reorder controls. The cards are the source of truth for the setting they edit, the way form
/// inputs are for a setting that is a single value. The consumer supplies the collapsed header, the
/// expanded body and any extra header actions as templates, and the list it edits as ItemsSource.
/// </summary>
public sealed partial class CardListView : UserControl
{
    private readonly List<CardEntry> _cards = new();
    private readonly IStringLocalizer _stringLocalizer;

    private CardDragState? _dragState;

    // Set while the drop writes the new order back, so the collection change it raises does not rebuild
    // the cards out from under the drag that just placed them.
    private bool _isCommittingOrder;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IList),
        typeof(CardListView),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty HeaderTemplateProperty = DependencyProperty.Register(
        nameof(HeaderTemplate),
        typeof(DataTemplate),
        typeof(CardListView),
        new PropertyMetadata(null, OnTemplateChanged));

    public static readonly DependencyProperty BodyTemplateProperty = DependencyProperty.Register(
        nameof(BodyTemplate),
        typeof(DataTemplate),
        typeof(CardListView),
        new PropertyMetadata(null, OnTemplateChanged));

    public static readonly DependencyProperty HeaderActionsTemplateProperty = DependencyProperty.Register(
        nameof(HeaderActionsTemplate),
        typeof(DataTemplate),
        typeof(CardListView),
        new PropertyMetadata(null, OnTemplateChanged));

    public static readonly DependencyProperty AddButtonTextProperty = DependencyProperty.Register(
        nameof(AddButtonText),
        typeof(string),
        typeof(CardListView),
        new PropertyMetadata(string.Empty, OnAddButtonTextChanged));

    public static readonly DependencyProperty AddRowContentProperty = DependencyProperty.Register(
        nameof(AddRowContent),
        typeof(object),
        typeof(CardListView),
        new PropertyMetadata(null, OnAddRowContentChanged));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(CardListView),
        new PropertyMetadata(string.Empty, OnEmptyTextChanged));

    /// <summary>
    /// The list the cards edit. Delete and reorder write straight to it, so it must be mutable; supply an
    /// ObservableCollection for the cards to follow additions the consumer makes.
    /// </summary>
    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// The content of a card's collapsed header, which identifies the entry.
    /// </summary>
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>
    /// The content a card reveals when it is expanded, which edits the entry.
    /// </summary>
    public DataTemplate? BodyTemplate
    {
        get => (DataTemplate?)GetValue(BodyTemplateProperty);
        set => SetValue(BodyTemplateProperty, value);
    }

    /// <summary>
    /// Extra controls placed in the header ahead of the delete button, for actions that should be reachable
    /// without expanding the card.
    /// </summary>
    public DataTemplate? HeaderActionsTemplate
    {
        get => (DataTemplate?)GetValue(HeaderActionsTemplateProperty);
        set => SetValue(HeaderActionsTemplateProperty, value);
    }

    /// <summary>
    /// The label of the button that asks for a new entry.
    /// </summary>
    public string AddButtonText
    {
        get => (string)GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    /// <summary>
    /// Extra controls placed in the add row, ahead of the button that asks for a new entry, for actions
    /// that add an entry some other way.
    /// </summary>
    public object? AddRowContent
    {
        get => GetValue(AddRowContentProperty);
        set => SetValue(AddRowContentProperty, value);
    }

    /// <summary>
    /// The message shown in place of the cards while the list is empty.
    /// </summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>
    /// Raised when the user asks for a new entry. Only the consumer knows what a blank entry is, so it adds
    /// the item to ItemsSource itself.
    /// </summary>
    public event EventHandler? AddRequested;

    // The shared icon scale, read from the application resources so the cards carry the same glyph size as
    // the rest of the chrome.
    private static double IconSize => (double)Application.Current.Resources["IconSizeMedium"];

    public CardListView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();

        LayoutRoot.PointerMoved += LayoutRoot_PointerMoved;
        LayoutRoot.PointerReleased += LayoutRoot_PointerReleased;
        LayoutRoot.PointerCaptureLost += LayoutRoot_PointerCaptureLost;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cardListView = (CardListView)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= cardListView.ItemsSource_CollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += cardListView.ItemsSource_CollectionChanged;
        }

        cardListView.RebuildCards();
    }

    private static void OnTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cardListView = (CardListView)d;
        cardListView.RebuildCards();
    }

    private static void OnAddButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cardListView = (CardListView)d;
        cardListView.AddButton.Content = cardListView.AddButtonText;
    }

    private static void OnAddRowContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cardListView = (CardListView)d;
        cardListView.AddRowPresenter.Content = cardListView.AddRowContent;
    }

    private static void OnEmptyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cardListView = (CardListView)d;
        cardListView.EmptyTextBlock.Text = cardListView.EmptyText;
    }

    private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isCommittingOrder)
        {
            return;
        }

        RebuildCards();

        // An entry the user just asked for opens ready to fill in, which the rebuild would otherwise leave
        // closed like the rest.
        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewStartingIndex >= 0
            && e.NewStartingIndex < _cards.Count)
        {
            _cards[e.NewStartingIndex].Card.IsExpanded = true;
        }
    }

    private void RebuildCards()
    {
        CancelDrag();

        _cards.Clear();
        CardsPanel.Children.Clear();

        var items = ItemsSource;
        if (items is not null)
        {
            foreach (var item in items)
            {
                if (item is null)
                {
                    continue;
                }

                var entry = CreateCard(item, items.Count);
                _cards.Add(entry);
                CardsPanel.Children.Add(entry.Container);
            }
        }

        EmptyTextBlock.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private CardEntry CreateCard(object item, int itemCount)
    {
        var transform = new TranslateTransform();

        var headerGrid = new Grid
        {
            ColumnSpacing = 8
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var card = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Header = headerGrid,
            Content = new ContentPresenter
            {
                Content = item,
                ContentTemplate = BodyTemplate
            }
        };

        // A single card has nothing to reorder against, so it carries no grip.
        Icon? gripIcon = null;
        if (itemCount > 1)
        {
            gripIcon = new Icon
            {
                IconName = "bs-grip-vertical",
                FontSize = IconSize
            };

            var grip = CreateGrip(card, gripIcon);
            Grid.SetColumn(grip, 0);
            headerGrid.Children.Add(grip);
        }

        var headerContent = new ContentPresenter
        {
            Content = item,
            ContentTemplate = HeaderTemplate,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerContent, 1);
        headerGrid.Children.Add(headerContent);

        var actions = CreateHeaderActions(item);
        Grid.SetColumn(actions, 2);
        headerGrid.Children.Add(actions);

        // Every visual state of the stock header animates its border, and an animated value cannot be
        // overridden, so the held card is marked with a ring drawn over that border rather than by changing
        // it. Same bounds and corners, so it reads as the border taking the accent.
        var outline = new Border
        {
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };

        var container = new Grid
        {
            RenderTransform = transform
        };
        container.Children.Add(card);
        container.Children.Add(outline);

        return new CardEntry(item, container, card, outline, transform, gripIcon);
    }

    private FrameworkElement CreateGrip(Expander card, Icon gripIcon)
    {
        var grip = new Border
        {
            Padding = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = card,
            Child = gripIcon
        };

        ToolTipService.SetToolTip(grip, _stringLocalizer.GetString("CardList_Reorder").Value);

        grip.PointerPressed += Grip_PointerPressed;

        return grip;
    }

    private StackPanel CreateHeaderActions(object item)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (HeaderActionsTemplate is not null)
        {
            actions.Children.Add(new ContentPresenter
            {
                Content = item,
                ContentTemplate = HeaderActionsTemplate,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var deleteButton = new IconButton
        {
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item,
            Content = new Icon
            {
                Symbol = IconSymbol.Delete,
                FontSize = IconSize
            }
        };
        ToolTipService.SetToolTip(deleteButton, _stringLocalizer.GetString("CardList_Delete").Value);
        deleteButton.Click += DeleteButton_Click;
        actions.Children.Add(deleteButton);

        // The header sits inside the expander's own toggle, which would otherwise treat a press on one of
        // these controls as a request to open the card.
        actions.PointerPressed += Actions_PointerHandled;
        actions.PointerReleased += Actions_PointerHandled;

        return actions;
    }

    private void Actions_PointerHandled(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var deleteButton = (FrameworkElement)sender;
        var item = deleteButton.Tag;

        var items = ItemsSource;
        if (items is null
            || item is null)
        {
            return;
        }

        items.Remove(item);
    }

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;

        if (_dragState is not null
            || _cards.Count < 2)
        {
            return;
        }

        // A drag runs against a collapsed list, because uniform rows are what the placement arithmetic
        // needs. Collapsing as part of the grab would shorten everything above the grabbed row and slide it
        // out from under the pointer, so the first press on a grip while anything is open only tidies the
        // list; the user presses again to drag.
        if (CollapseExpandedCards())
        {
            return;
        }

        var grip = (FrameworkElement)sender;
        var card = (Expander)grip.Tag;

        var entry = _cards.FirstOrDefault(candidate => candidate.Card == card);
        if (entry is null)
        {
            return;
        }

        var originalIndex = _cards.IndexOf(entry);
        var cardTops = _cards.Select(candidate => TopInLayoutRoot(candidate.Container)).ToList();
        var pointerY = e.GetCurrentPoint(LayoutRoot).Position.Y;

        // Measured between two rows rather than from one row's height, so it takes in the spacing between
        // the cards as well.
        var rowPitch = cardTops[1] - cardTops[0];

        if (!LayoutRoot.CapturePointer(e.Pointer))
        {
            return;
        }

        _dragState = new CardDragState
        {
            Entry = entry,
            Pointer = e.Pointer,
            OriginalIndex = originalIndex,
            GrabOffset = pointerY - cardTops[originalIndex],
            RowPitch = rowPitch,
            SlotIndex = originalIndex
        };

        SetHeldAppearance(entry, isHeld: true);
    }

    // Marks the card the pointer is carrying: an accent outline and grip, and a lift over its neighbours,
    // since it follows the pointer and so spends most of a drag straddling two rows. The web card list
    // marks a held card the same way.
    private void SetHeldAppearance(CardEntry entry, bool isHeld)
    {
        Canvas.SetZIndex(entry.Container, isHeld ? 1 : 0);

        if (isHeld)
        {
            // Read now rather than at build time, the expander taking its corners from a style that is
            // resolved once the card is in the tree.
            entry.Outline.CornerRadius = entry.Card.CornerRadius;
            entry.Outline.BorderBrush = (Brush)Resources["CardDragOutlineBrush"];
        }
        else
        {
            entry.Outline.ClearValue(Border.BorderBrushProperty);
        }

        if (entry.GripIcon is null)
        {
            return;
        }

        if (isHeld)
        {
            entry.GripIcon.Foreground = (Brush)Resources["CardDragGripBrush"];
            return;
        }

        entry.GripIcon.ClearValue(IconElement.ForegroundProperty);
    }

    // Collapses every open card, reporting whether any was open.
    private bool CollapseExpandedCards()
    {
        var wasAnyExpanded = false;

        foreach (var entry in _cards)
        {
            if (!entry.Card.IsExpanded)
            {
                continue;
            }

            entry.Card.IsExpanded = false;
            wasAnyExpanded = true;
        }

        return wasAnyExpanded;
    }

    private void LayoutRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragState is null)
        {
            return;
        }

        e.Handled = true;

        var measurements = new CardReorderMeasurements(
            PointerY: e.GetCurrentPoint(LayoutRoot).Position.Y,
            GrabOffset: _dragState.GrabOffset,
            // Read live: the panel's own position is unaffected by the rows reordering inside it, but it
            // does follow the settings surface scrolling under the drag.
            ListTop: TopInLayoutRoot(CardsPanel),
            RowPitch: _dragState.RowPitch,
            RowCount: _cards.Count);

        var placement = CardReorder.PlacementForPointer(measurements);
        if (placement is null)
        {
            return;
        }

        ApplySlot(placement.SlotIndex);

        // A render transform does not affect layout, so holding the card away from its slot neither moves
        // the rows around it nor feeds back into the next placement.
        _dragState.Entry.Transform.Y = placement.Offset;
    }

    private void LayoutRoot_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragState is null)
        {
            return;
        }

        e.Handled = true;

        // Recorded before the capture is released, which raises PointerCaptureLost synchronously.
        var pointer = _dragState.Pointer;
        CompleteDrag();

        LayoutRoot.ReleasePointerCapture(pointer);
    }

    private void LayoutRoot_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // The pointer was taken over mid-drag. The cards are already in the arrangement the last move
        // placed them in, so that arrangement is what gets recorded.
        CompleteDrag();
    }

    // Moves the dragged card into a slot. The other cards hold their relative order for the whole drag, so
    // placing the card at the target index always produces the same arrangement, whatever the list
    // currently looks like. That makes reapplying a slot a no-op and leaves no incremental state to drift.
    private void ApplySlot(int slotIndex)
    {
        if (_dragState is null
            || slotIndex == _dragState.SlotIndex)
        {
            return;
        }

        _dragState.SlotIndex = slotIndex;

        var entry = _dragState.Entry;

        _cards.Remove(entry);
        _cards.Insert(slotIndex, entry);

        CardsPanel.Children.Remove(entry.Container);
        CardsPanel.Children.Insert(slotIndex, entry.Container);
    }

    private void CompleteDrag()
    {
        if (_dragState is null)
        {
            return;
        }

        var dragState = _dragState;
        _dragState = null;

        dragState.Entry.Transform.Y = 0;
        SetHeldAppearance(dragState.Entry, isHeld: false);

        if (dragState.SlotIndex == dragState.OriginalIndex)
        {
            return;
        }

        var items = ItemsSource;
        if (items is null)
        {
            return;
        }

        // The cards already sit in the new order, so the list is brought into line with them rather than
        // the other way round.
        _isCommittingOrder = true;
        try
        {
            var item = dragState.Entry.Item;
            items.Remove(item);
            items.Insert(dragState.SlotIndex, item);
        }
        finally
        {
            _isCommittingOrder = false;
        }
    }

    // Abandons a drag without recording it, for a rebuild that is about to discard the cards it is holding.
    private void CancelDrag()
    {
        if (_dragState is null)
        {
            return;
        }

        var dragState = _dragState;

        // Cleared before the capture is released, so the PointerCaptureLost that raises finds no drag to
        // record rather than committing the one being abandoned.
        _dragState = null;
        dragState.Entry.Transform.Y = 0;
        SetHeldAppearance(dragState.Entry, isHeld: false);

        LayoutRoot.ReleasePointerCapture(dragState.Pointer);
    }

    private double TopInLayoutRoot(UIElement element)
    {
        var transform = element.TransformToVisual(LayoutRoot);

        return transform.TransformPoint(new Point(0, 0)).Y;
    }
}
