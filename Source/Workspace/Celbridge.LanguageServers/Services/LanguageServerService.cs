using Celbridge.LanguageServers.Helpers;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// Runs the language servers of the loaded project, one for each server definition in use.
/// </summary>
public sealed class LanguageServerService : ILanguageServerService, IDisposable
{
    private const int CrashLimit = 3;
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyList<ILanguageServerDefinition> _definitions;
    private readonly ILanguageServerLauncher _launcher;
    private readonly IFeatureFlags _featureFlags;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILogger<LanguageServerService> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, LanguageServerSession> _sessions = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _crashTimes = new();
    private readonly HashSet<string> _stoppedServerIds = new();
    private bool _disposed;

    public LanguageServerService(
        IEnumerable<ILanguageServerDefinition> definitions,
        IServiceProvider serviceProvider,
        IFeatureFlags featureFlags,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IAppEnvironment appEnvironment,
        ILogger<LanguageServerService> logger)
        : this(
            definitions,
            serviceProvider.GetRequiredService<ILanguageServerLauncher>(),
            featureFlags,
            projectService,
            workspaceWrapper,
            appEnvironment,
            logger,
            TimeProvider.System)
    {
    }

    internal LanguageServerService(
        IEnumerable<ILanguageServerDefinition> definitions,
        ILanguageServerLauncher launcher,
        IFeatureFlags featureFlags,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        IAppEnvironment appEnvironment,
        ILogger<LanguageServerService> logger,
        TimeProvider timeProvider)
    {
        _definitions = definitions.ToList();
        _launcher = launcher;
        _featureFlags = featureFlags;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _appEnvironment = appEnvironment;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public string GetServerId(ResourceKey resource)
    {
        var document = FindServedDocument(resource);
        if (document is null)
        {
            return string.Empty;
        }

        return document.Definition.Id;
    }

    public ILanguagePageConnection ConnectPage(ResourceKey resource)
    {
        var document = FindServedDocument(resource);
        if (document is null)
        {
            return new RefusedPageConnection();
        }

        var definition = document.Definition;
        var documentUri = LspUri.FromPath(document.FilePath);

        lock (_lock)
        {
            if (_disposed
                || _stoppedServerIds.Contains(definition.Id))
            {
                return new RefusedPageConnection();
            }

            if (_sessions.TryGetValue(definition.Id, out var runningSession))
            {
                var page = runningSession.AddPage(documentUri, document.LanguageId);
                if (page is not null)
                {
                    return page;
                }

                // The session has ended, and its report of the end has not arrived yet.
                _sessions.Remove(definition.Id);
            }

            var startResult = StartSession(definition, document.ProjectFolderPath);
            if (startResult.IsFailure)
            {
                _logger.LogWarning(startResult, $"Failed to start the language server '{definition.Id}'");
                RecordCrash(definition.Id);
                return new RefusedPageConnection();
            }

            var session = startResult.Value;
            _sessions[definition.Id] = session;

            var newPage = session.AddPage(documentUri, document.LanguageId);
            if (newPage is null)
            {
                return new RefusedPageConnection();
            }

            return newPage;
        }
    }

    private sealed record ServedDocument(
        ILanguageServerDefinition Definition,
        string FilePath,
        string LanguageId,
        string ProjectFolderPath);

    // Returns the server definition, path and language of a document inside the project folder. Returns null when
    // no definition covers the document's extension, or when the definition's feature is off.
    private ServedDocument? FindServedDocument(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource.ToString()).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        var project = _projectService.CurrentProject;
        if (project is null
            || !_workspaceWrapper.HasWorkspaceService)
        {
            return null;
        }

        foreach (var definition in _definitions)
        {
            if (!definition.LanguageIds.TryGetValue(extension, out var languageId)
                || !_featureFlags.IsEnabled(definition.Feature))
            {
                continue;
            }

            var rootHandlers = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
            var resolveResult = rootHandlers.ResolveResourcePath(resource);
            if (resolveResult.IsFailure)
            {
                return null;
            }
            var filePath = resolveResult.Value;

            // A server reads the project folder, so a file outside the folder gets no server.
            var projectFolderPath = Path.TrimEndingDirectorySeparator(project.ProjectFolderPath);
            var projectFolderPrefix = projectFolderPath + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(projectFolderPrefix, PathComparison.Comparison))
            {
                return null;
            }

            return new ServedDocument(definition, filePath, languageId, projectFolderPath);
        }

        return null;
    }

    private Result<LanguageServerSession> StartSession(ILanguageServerDefinition definition, string projectFolderPath)
    {
        var invocation = definition.CreateInvocation();

        var launchResult = _launcher.Start(invocation, projectFolderPath);
        if (launchResult.IsFailure)
        {
            return Result<LanguageServerSession>.Fail($"Failed to launch the language server '{definition.Id}'")
                .WithErrors(launchResult);
        }
        var process = launchResult.Value;

        var clientVersion = _appEnvironment.GetEnvironmentInfo().AppVersion;
        var session = new LanguageServerSession(
            definition,
            process,
            projectFolderPath,
            clientVersion,
            _logger,
            _timeProvider,
            OnSessionEnded);

        session.Start();
        _logger.LogInformation("Started the language server '{ServerId}'", definition.Id);

        return session;
    }

    private void OnSessionEnded(LanguageServerSession session, bool crashed)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(session.ServerId, out var currentSession)
                && currentSession == session)
            {
                _sessions.Remove(session.ServerId);
            }

            if (crashed)
            {
                RecordCrash(session.ServerId);
            }
        }
    }

    // Counts the server's unexpected ends within the crash window, and stops the server for good at the limit.
    private void RecordCrash(string serverId)
    {
        if (!_crashTimes.TryGetValue(serverId, out var crashTimes))
        {
            crashTimes = new List<DateTimeOffset>();
            _crashTimes[serverId] = crashTimes;
        }

        var now = _timeProvider.GetUtcNow();
        crashTimes.Add(now);
        crashTimes.RemoveAll(crashTime => now - crashTime > CrashWindow);

        if (crashTimes.Count >= CrashLimit)
        {
            _stoppedServerIds.Add(serverId);
            _logger.LogWarning(
                "The language server '{ServerId}' ended unexpectedly {Count} times within {Seconds} seconds. It stays stopped until the project reloads.",
                serverId,
                crashTimes.Count,
                CrashWindow.TotalSeconds);
        }
    }

    public void Dispose()
    {
        List<LanguageServerSession> sessions;

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = _sessions.Values.ToList();
        }

        // A server takes time to exit, so each stop runs off the calling thread.
        foreach (var session in sessions)
        {
            _ = Task.Run(session.StopAsync);
        }
    }
}
