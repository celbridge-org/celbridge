using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// Gives each editor of the code editor package a language channel.
/// </summary>
public sealed class LanguageChannelProvider : ICustomEditorChannelProvider
{
    private const string CodeEditorPackageName = "celbridge-code-editor";

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<LanguageChannelProvider> _logger;

    public LanguageChannelProvider(IWorkspaceWrapper workspaceWrapper, ILogger<LanguageChannelProvider> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    public bool CanCreate(EditorContribution contribution)
    {
        return contribution.Package.Name == CodeEditorPackageName;
    }

    public ICustomEditorChannel Create(CustomEditorChannelContext context)
    {
        return new LanguageChannel(_workspaceWrapper, _logger);
    }
}
