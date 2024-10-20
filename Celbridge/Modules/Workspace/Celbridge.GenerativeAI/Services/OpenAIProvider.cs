using OpenAI.Chat;

namespace Celbridge.GenerativeAI;

public class OpenAIProvider : IGenerativeAIProvider
{
    private readonly ChatClient _chatClient;

    public OpenAIProvider()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _chatClient = new ChatClient("gpt-4o", apiKey);
    }

    public async Task<Result<string>> GenerateTextAsync(string input)
    {
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
