namespace Celbridge.Legacy.OpenAI;

public class OpenAIUtilities
{
    private readonly HttpClient _httpClient;

    public OpenAIUtilities()
    {
        _httpClient = new HttpClient();
    }

    public async Task<byte[]> ConvertTextToSpeechAsync(string apiKey, string text, string model = "tts-1", string voice = "alloy")
    {
        // Configure the HttpClient to use the OpenAI API key
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        // Prepare the JSON body for the request
        var requestBody = new JObject
        {
            ["model"] = model,
            ["input"] = text,
            ["voice"] = voice
        };

        try
        {
            // Send a POST request to the OpenAI API
            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/audio/speech",
                new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json")
            );

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }
            else
            {
                // In case of API errors
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception("Error from OpenAI API: " + errorContent);
            }
        }
        catch (Exception ex)
        {
            // Handle any exceptions (network errors, JSON parsing errors, etc.)
            throw new Exception("An error occurred while processing the text-to-speech request: " + ex.Message, ex);
        }
    }
}
