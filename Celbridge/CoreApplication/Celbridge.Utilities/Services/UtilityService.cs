using Celbridge.BaseLibrary.Utilities;

namespace Celbridge.Messaging.Services;

public class UtilityService : IUtilityService
{
    public bool IsPathSegmentValid(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        // It's very difficult to robustly check for invalid characters in a way that works for every
        // platform. We do a basic check for invalid characters on the current platform.
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in segment)
        {
            if (invalidChars.Contains(c))
            {
                return false;
            }
        }

        return true;
    }
}
