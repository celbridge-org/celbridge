namespace Celbridge.Markdown;

public class Extension : IExtension
{
    private IExtensionContext? _context;
    public IExtensionContext Context => _context!;

    public Result Initialize(IExtensionContext context)
    {
        _context = context;

        // It's up to the extension author to pass the extension context to any code that needs it.

        return Result.Ok();
    }
}

