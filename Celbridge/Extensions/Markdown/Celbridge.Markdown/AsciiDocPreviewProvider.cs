namespace Celbridge.Markdown;

public class AsciiDocPreviewProvider : PreviewProvider
{
    public AsciiDocPreviewProvider()
    {
        SupportedFileExtensions.Add(".adoc");
    }

    public override async Task<Result<string>> GeneratePreview(string text, IEditorPreview editorPreview)
    {
        // Todo: Refactor this - I'm using the AsciiDoctor.js library in the preview webview to do the conversion.
        var convertResult = await editorPreview.ConvertAsciiDocToHTML(text);

        if (convertResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to convert text to AsciiDoc");
        }
        var html = convertResult.Value;

        return Result<string>.Ok(html);
    }
}
