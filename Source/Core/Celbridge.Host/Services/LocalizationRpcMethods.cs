namespace Celbridge.Host;

public static class LocalizationRpcMethods
{
    // Host to client. Tells the page the application language changed, so it can reload its strings without
    // waiting for the next launch as the native user interface does.
    public const string LanguageChanged = "localization/languageChanged";
}

public static class HostLocalizationExtensions
{
    /// <summary>
    /// Tells the page the application is now presenting the given language, so it can reload its strings.
    /// </summary>
    public static Task NotifyLanguageChangedAsync(this CelbridgeHost host, string locale)
        => host.Rpc.NotifyWithParameterObjectAsync(LocalizationRpcMethods.LanguageChanged, new { locale });
}
