namespace Celbridge.Markdown;

public class MarkdownExtension : ExtensionBase
{
    public override Result OnLoadExtension()
    {
        var markdownPreviewProvider = new MarkdownPreviewProvider();
        Context.PreviewProviders.Add(".md", markdownPreviewProvider);

        return Result.Ok();
    }

    public override Result OnUnloadExtension()
    {
        return Result.Ok();
    }
}
