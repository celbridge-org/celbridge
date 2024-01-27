namespace Celbridge.Legacy.Utils;

class ResourceUtils
{
    public static T? Get<T>(string resourceName)
    {
        try
        {
            var success = Application.Current.Resources.TryGetValue(resourceName, out var outValue);
            if (success && outValue is T)
            {
                return (T)outValue;
            }
            else
            {
                return default(T);
            }
        }
        catch
        {
            return default(T);
        }
    }
}
