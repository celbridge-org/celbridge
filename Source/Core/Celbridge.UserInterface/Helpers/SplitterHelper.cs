namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper class to manage splitter drag operations for resizing columns or rows in a Grid.
/// </summary>
public class SplitterHelper
{
    private readonly Grid _grid;
    private readonly GridResizeMode _mode;
    private readonly int _firstIndex;
    private readonly int _secondIndex;
    private readonly double _minSize;
    private readonly Func<double>? _maxSizeFunc;
    private readonly SplitterResizeMode _resizeMode;

    private double _firstStartSize;
    private double _secondStartSize;

    /// <summary>
    /// Creates a new SplitterHelper for managing splitter drag operations with paired resizing.
    /// </summary>
    public SplitterHelper(
        Grid grid,
        GridResizeMode mode,
        int firstIndex,
        int secondIndex,
        double minSize = 200)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _mode = mode;
        _firstIndex = firstIndex;
        _secondIndex = secondIndex;
        _minSize = minSize;
        _resizeMode = SplitterResizeMode.Paired;
    }

    /// <summary>
    /// Creates a new SplitterHelper for managing splitter drag operations with single panel resizing.
    /// </summary>
    public SplitterHelper(
        Grid grid,
        GridResizeMode mode,
        int index,
        double minSize = 100,
        bool invertDelta = false,
        Func<double>? maxSizeFunc = null)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _mode = mode;
        _firstIndex = index;
        _secondIndex = invertDelta ? -1 : 0; // Use secondIndex == -1 to indicate inverted delta
        _minSize = minSize;
        _maxSizeFunc = maxSizeFunc;
        _resizeMode = SplitterResizeMode.Single;
    }

    /// <summary>
    /// Call this when the splitter drag starts to capture the current sizes.
    /// </summary>
    public void OnDragStarted()
    {
        if (_mode == GridResizeMode.Columns)
        {
            if (_firstIndex < _grid.ColumnDefinitions.Count)
            {
                _firstStartSize = _grid.ColumnDefinitions[_firstIndex].ActualWidth;

                if (_resizeMode == SplitterResizeMode.Paired && _secondIndex < _grid.ColumnDefinitions.Count)
                {
                    _secondStartSize = _grid.ColumnDefinitions[_secondIndex].ActualWidth;
                }
            }
        }
        else // Rows
        {
            if (_firstIndex < _grid.RowDefinitions.Count)
            {
                _firstStartSize = _grid.RowDefinitions[_firstIndex].ActualHeight;

                if (_resizeMode == SplitterResizeMode.Paired && _secondIndex < _grid.RowDefinitions.Count)
                {
                    _secondStartSize = _grid.RowDefinitions[_secondIndex].ActualHeight;
                }
            }
        }
    }

    /// <summary>
    /// Call this when the splitter is dragged to calculate and apply the new sizes.
    /// </summary>
    public void OnDragDelta(double delta)
    {
        if (_resizeMode == SplitterResizeMode.Single)
        {
            // Single panel resize mode
            var adjustedDelta = _secondIndex == -1 ? -delta : delta; // Invert if secondIndex == -1
            var newSize = _firstStartSize + adjustedDelta;

            if (newSize < _minSize)
            {
                return;
            }

            if (_maxSizeFunc != null)
            {
                var maxSize = _maxSizeFunc();
                if (newSize > maxSize)
                {
                    newSize = maxSize;
                }
            }

            if (_mode == GridResizeMode.Columns)
            {
                if (_firstIndex < _grid.ColumnDefinitions.Count)
                {
                    _grid.ColumnDefinitions[_firstIndex].Width = new GridLength(newSize, GridUnitType.Pixel);
                }
            }
            else // Rows
            {
                if (_firstIndex < _grid.RowDefinitions.Count)
                {
                    _grid.RowDefinitions[_firstIndex].Height = new GridLength(newSize, GridUnitType.Pixel);
                }
            }
        }
        else // Paired resize mode
        {
            // Calculate new sizes
            var newFirstSize = _firstStartSize + delta;
            var newSecondSize = _secondStartSize - delta;

            // Enforce minimum sizes
            if (newFirstSize < _minSize || newSecondSize < _minSize)
            {
                return;
            }

            // Apply the new sizes
            if (_mode == GridResizeMode.Columns)
            {
                if (_firstIndex < _grid.ColumnDefinitions.Count && _secondIndex < _grid.ColumnDefinitions.Count)
                {
                    _grid.ColumnDefinitions[_firstIndex].Width = new GridLength(newFirstSize, GridUnitType.Pixel);
                    _grid.ColumnDefinitions[_secondIndex].Width = new GridLength(newSecondSize, GridUnitType.Pixel);
                }
            }
            else // Rows
            {
                if (_firstIndex < _grid.RowDefinitions.Count && _secondIndex < _grid.RowDefinitions.Count)
                {
                    _grid.RowDefinitions[_firstIndex].Height = new GridLength(newFirstSize, GridUnitType.Pixel);
                    _grid.RowDefinitions[_secondIndex].Height = new GridLength(newSecondSize, GridUnitType.Pixel);
                }
            }
        }
    }
}

/// <summary>
/// Specifies whether the SplitterHelper should resize columns or rows.
/// </summary>
public enum GridResizeMode
{
    Columns,
    Rows
}

/// <summary>
/// Specifies whether the SplitterHelper should resize a single panel or a pair of panels.
/// </summary>
internal enum SplitterResizeMode
{
    Single,
    Paired
}
