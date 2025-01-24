namespace Celbridge.GenerativeAI;

/// <summary>
/// A generative AI provider, such as OpenAI, Google Gemini, etc.
/// </summary>
public interface IGenerativeAIProvider
{
    /// <summary>
    /// Generates text based on the provided input prompt.
    /// </summary>
    Task<Result<string>> GenerateTextAsync(string input);
}
