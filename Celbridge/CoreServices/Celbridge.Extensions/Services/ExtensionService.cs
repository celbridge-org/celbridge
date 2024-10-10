using Celbridge.Foundation;

namespace Celbridge.Extensions.Services;

public class ExtensionService : IExtensionService
{
    public ExtensionService()
    {}

    public Result LoadExtensions(List<string> extensions)
    {
        // Todo: Load the assemblies
        // Todo: Find the extension classes
        // Todo: Instantiate the extension classes
        // Todo: Call the Initialize methods

        return Result.Ok();
    }

    public Result UnloadExtensions()
    {
        // Todo: Call dispose on all extension classes
        // Note: I'm not going to bother using an assembly context, process isolation is the best way to handle this in future.

        return Result.Ok();
    }
}
