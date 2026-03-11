namespace Celbridge.Search.ViewModels;

/// <summary>
/// Interface for selectable items in search results (file headers and match lines).
/// Enables type-safe selection handling across both item types.
/// </summary>
public interface ISelectableSearchItem
{
    bool IsSelected { get; set; }
}
