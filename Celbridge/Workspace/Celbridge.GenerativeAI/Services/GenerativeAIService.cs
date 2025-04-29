using Celbridge.Commands;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.GenerativeAI;

public class GenerativeAIService : IGenerativeAIService, IDisposable
{
    private readonly ICommandService _commandService;
    private readonly IGenerativeAIProvider _aiProvider;

    public GenerativeAIService(
        IServiceProvider serviceProvider,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _commandService = commandService;

        // Todo: Configure provider for the project
        _aiProvider = serviceProvider.GetRequiredService<IGenerativeAIProvider>();
    }

    public async Task<Result<string>> GenerateTextAsync(string prompt)
    {
        try
        {
            var generateResult = await _aiProvider.GenerateTextAsync(prompt);
            if (generateResult.IsFailure)
            {
                return Result<string>.Fail("Failed to generate text")
                    .WithErrors(generateResult);
            }
            var content = generateResult.Value;

            return Result<string>.Ok(content);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail("An exception occurred when generating text")
                .WithException(ex);
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
            }

            _disposed = true;
        }
    }

    ~GenerativeAIService()
    {
        Dispose(false);
    }
}
