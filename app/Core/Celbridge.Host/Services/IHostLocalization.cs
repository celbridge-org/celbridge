namespace Celbridge.Host;

public static class LocalizationRpcMethods
{
    public const string Updated = "localization/updated";
}

/// <summary>
/// RPC service interface for localization
/// </summary>
public interface IHostLocalization
{ }

public static class HostLocalizationExtensions
{
    /// <summary>
    /// Notifies the WebView that localization strings have been updated.
    /// </summary>
    public static Task NotifyLocalizationUpdatedAsync(this CelbridgeHost host, Dictionary<string, string> strings)
        => host.Rpc.NotifyWithParameterObjectAsync(LocalizationRpcMethods.Updated, new { strings });
}
