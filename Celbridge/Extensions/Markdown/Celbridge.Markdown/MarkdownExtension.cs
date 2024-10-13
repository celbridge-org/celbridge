namespace Celbridge.Markdown;

public class MarkdownExtension : Extension
{
    public override Result OnLoadExtension()
    {
        var markdownPreviewProvider = new MarkdownPreviewProvider();
        var addResult = Context.AddPreviewProvider(markdownPreviewProvider);
        if (addResult.IsFailure)
        {
            return Result.Fail("Failed to add Markdown preview provider.")
                .AddErrors(addResult);
        }

        return Result.Ok();
    }

    public override Result OnUnloadExtension()
    {
        return Result.Ok();
    }
}
