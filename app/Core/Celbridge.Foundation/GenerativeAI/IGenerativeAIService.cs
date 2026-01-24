namespace Celbridge.GenerativeAI;

/// <summary>
/// Service for generating file resources using generative AI.
/// </summary>
public interface IGenerativeAIService
{
    /// <summary>
    /// Generates text based on the provided prompt.
    /// </summary>
    Task<Result<string>> GenerateTextAsync(string prompt);
}
