using Celbridge.Packages;
using Celbridge.WebHost;

namespace Celbridge.Console.Services;

/// <summary>
/// Supplies the channel for the console document editor. Matches the console package by name.
/// Creating a channel is cheap and non-throwing so a launch failure surfaces in the document, not at open.
/// </summary>
public sealed class ConsoleSessionChannelProvider : ICustomEditorChannelProvider
{
    // This provider only backs the console package, so it matches by name.
    private const string ConsolePackageName = "celbridge-console";

    private readonly IServiceProvider _serviceProvider;

    public ConsoleSessionChannelProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool CanCreate(EditorContribution contribution)
    {
        return contribution.Package.Name == ConsolePackageName;
    }

    public ICustomEditorChannel Create(CustomEditorChannelContext context)
    {
        return new ConsoleSessionChannel(_serviceProvider, context.FileResource);
    }
}
