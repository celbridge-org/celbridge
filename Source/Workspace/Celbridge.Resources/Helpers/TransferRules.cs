namespace Celbridge.Resources.Helpers;

/// <summary>
/// The rule a move or copy of a resource obeys before any other check: the destination lies outside the source.
/// The gateway applies the rule when the operation runs. The Explorer's drop check and the transfer service apply
/// the rule ahead of the operation, so each refuses what the operation refuses.
/// </summary>
public static class TransferRules
{
    /// <summary>
    /// Returns a failure when the destination lies inside the source. A folder moved or copied there would contain
    /// itself. The source itself is an accepted destination, since a copy onto itself makes a duplicate.
    /// </summary>
    public static Result CheckDestination(ResourceKey source, ResourceKey dest)
    {
        if (dest.IsDescendantOf(source))
        {
            return Result.Fail($"Cannot move or copy '{source}' into itself: '{dest}' is inside it.");
        }

        return Result.Ok();
    }
}
