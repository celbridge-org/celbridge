using Celbridge.Core;
using Celbridge.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Extensions.Services;

public class ExtensionService : IExtensionService
{
    private ILogger<ExtensionService> _logger;

    private static ExtensionLoader _extensionLoader = new();

    public ExtensionService(ILogger<ExtensionService> logger)
    {
        _logger = logger;
        _logger.LogInformation("ExtensionService created");
    }

    public Result InitializeExtensions()
    {
        return _extensionLoader.InitializeExtensions();
    }

    public static Result LoadExtensions(List<string> extensions, IServiceCollection services)
    {
        // Load Extensions
        foreach (var extension in extensions)
        {
            var loadResult = _extensionLoader.LoadExtension(extension);
            if (loadResult.IsFailure)
            {
                var failure = Result.Fail($"Failed to load extension {extension}");
                failure.MergeErrors(loadResult);
                return failure;
            }
        }

        var loadedExtensions = _extensionLoader.LoadedExtensions.Values.ToList();

        // Register the services provided by each extension with the dependency injection framework.
        var extensionServices = new ExtensionServiceCollection();
        foreach (var extension in loadedExtensions)
        {
            extension.ConfigureServices(extensionServices);
        }
        extensionServices.PopulateServices(services);

        return Result.Ok();
    }
}
