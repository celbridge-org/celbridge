namespace Celbridge.Extensions.Services;

public class ExtensionContext : IExtensionContext
{
    public Dictionary<string, IPreviewProvider> PreviewProviders { get; } = new();
}
