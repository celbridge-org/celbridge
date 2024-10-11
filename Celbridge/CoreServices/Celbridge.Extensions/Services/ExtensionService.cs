using Celbridge.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Celbridge.Extensions.Services;

public class ExtensionService : IExtensionService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExtensionService> _logger;

    public ExtensionService(
        IServiceProvider serviceProvider,
        ILogger<ExtensionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Dictionary<string, ExtensionBase> LoadedExtensions { get; } = new();

    public Result LoadExtension(string extension)
    {
        try
        {
            if (LoadedExtensions.ContainsKey(extension))
            {
                return Result.Fail($"Extension '{extension}' is already loaded.");
            }

            // Load the assembly containing the extension
            var assembly = Assembly.Load(extension);
            if (assembly == null)
            {
                return Result.Fail($"Failed to load assembly '{extension}'.");
            }

            // Find the class that implements IExtension
            var extensionType = assembly.GetTypes()
                .FirstOrDefault(type => typeof(ExtensionBase).IsAssignableFrom(type) && !type.IsAbstract);

            if (extensionType == null)
            {
                return Result.Fail($"No valid IExtension implementation found in assembly '{extension}'.");
            }

            // Instantiate the extension class
            var extensionInstance = (ExtensionBase)Activator.CreateInstance(extensionType)!;
            if (extensionInstance == null)
            {
                return Result.Fail($"Failed to instantiate extension class '{extension}'.");
            }

            // Initialize the extension with a new ExtensionContext.
            // The extension uses the ExtensionContext to interact with the Celbridge host application.
            var extensionContext = _serviceProvider.GetRequiredService<IExtensionContext>();
            var initResult = extensionInstance.Load(extensionContext);
            if (initResult.IsFailure)
            {
                return Result.Fail($"Failed to initialize extension: '{extension}'")
                    .AddErrors(initResult);
            }

            LoadedExtensions.Add(extension, extensionInstance);

            _logger.LogDebug($"Loaded extension: '{extension}'");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result<ExtensionBase>.Fail(ex, $"An exception occurred while loading the extension '{extension}'");
        }
    }

    public Result UnloadExtensions()
    {
        try
        {
            // Unload all extensions
            foreach (var extension in LoadedExtensions.Values)
            {
                var unloadResult = extension.Unload();
                if (unloadResult.IsFailure)
                {
                    var failure = Result.Fail($"Failed to unload extension: '{extension}'")
                        .AddErrors(unloadResult);
                    _logger.LogError(failure.Error);
                }
            }

            // Clear the list of loaded extensions
            LoadedExtensions.Clear();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occurred while unloading extensions");
        }
    }
}
