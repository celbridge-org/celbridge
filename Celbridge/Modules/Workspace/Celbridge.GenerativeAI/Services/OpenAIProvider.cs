using OpenAI.Chat;

namespace Celbridge.GenerativeAI;

public class OpenAIProvider : IGenerativeAIProvider
{
    private const string OpenAIKeyEnvironmentVariable = "OPENAI_API_KEY";

    public async Task<Result<string>> GenerateTextAsync(string input)
    {
        var apiKey = Environment.GetEnvironmentVariable(OpenAIKeyEnvironmentVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            return Result<string>.Fail($"Envirnonment variable not found: '{OpenAIKeyEnvironmentVariable}'");
        }

        var _chatClient = new ChatClient("gpt-4o", apiKey);
        var result = await _chatClient.CompleteChatAsync(input);

        var chatCompletion = result.Value;

        if (chatCompletion is null)
        {
            return Result<string>.Fail("Chat completion is empty");
        }

        var content = chatCompletion.Content[0].Text;
        if (string.IsNullOrEmpty(content))
        {
            return Result<string>.Fail("Chat completion is empty");
        }

        return Result<string>.Ok(content);
    }
}
