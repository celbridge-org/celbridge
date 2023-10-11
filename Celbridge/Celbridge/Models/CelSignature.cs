namespace Celbridge.Models
{
    public interface ICelSignature : IRecord
    {
        string GetSummary(PropertyContext context) => string.Empty;
        string ReturnType => string.Empty;
    }

    // An empty signature with no parameters
    public record CelSignature : ICelSignature
    {}
}
