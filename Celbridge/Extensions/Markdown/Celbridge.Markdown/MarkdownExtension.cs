namespace Celbridge.Markdown;

public class MarkdownExtension : Extension
{
    public override Result OnLoadExtension()
    {
        // Add Markdown preview provider
        var markdownPreviewProvider = new MarkdownPreviewProvider();
        var addMarkdownResult = Context.AddPreviewProvider(markdownPreviewProvider);
        if (addMarkdownResult.IsFailure)
        {
            return Result.Fail("Failed to add Markdown preview provider.")
                .WithErrors(addMarkdownResult);
        }

        // Add AsciiDoc preview provider
        var asciiDocPreviewProvider = new AsciiDocPreviewProvider();
        var addAsciiDocResult = Context.AddPreviewProvider(asciiDocPreviewProvider);
        if (addAsciiDocResult.IsFailure)
        {
            return Result.Fail("Failed to add AsciiDoc preview provider.")
                .WithErrors(addAsciiDocResult);
        }

        return Result.Ok();
    }

    public override Result OnUnloadExtension()
    {
        return Result.Ok();
    }
}
