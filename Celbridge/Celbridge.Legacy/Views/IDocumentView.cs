namespace Celbridge.Legacy.Views;

interface IDocumentView
{
    IDocument Document { get; set; }

    Task<Result> LoadDocumentAsync();

    void CloseDocument();
}
