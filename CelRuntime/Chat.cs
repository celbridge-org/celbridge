#nullable enable

using CelUtilities.ErrorHandling;
using CelUtilities.Resources;
using CommunityToolkit.Diagnostics;
using OpenAI_API;
using OpenAI_API.Chat;
using OpenAI_API.Images;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CelRuntime
{
    public class Chat
    {
        private OpenAIAPI? _api;
        private Conversation? _chat;

        private bool _isWaitingForResponse;

        public bool Init(string apiKey)
        {
            if (_api is not null)
            {
                return true;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Environment.PrintError("Failed to create Chat API. API key not found.");
                return false;
            }

            // https://github.com/OkGoDoIt/OpenAI-API-dotnet
            _api = new OpenAIAPI(apiKey);
            if (_api is null)
            {
                Environment.PrintError("Failed to create Chat API. API key not found.");
                return false;
            }

            return true;
        }

        public bool StartChat(string context)
        {
            Guard.IsNotNull(_api);

            _chat = _api.Chat.CreateConversation();
            if (_chat == null)
            {
                Environment.PrintError("Failed to create Chat API. API key not found.");
                return false;
            }

            _chat.AppendSystemMessage(context);

            return true;
        }

        public void EndChat()
        {
            _chat = null;
        }

        public async Task<string> Ask(string question)
        {
            // Wait for the previous response to be received
            while (_isWaitingForResponse)
            {
                await Task.Delay(100);
            }

            string response;

            try
            {
                Guard.IsNotNull(_chat);

                _chat.AppendUserInput(question);
                _isWaitingForResponse = true;
                response = await _chat.GetResponseFromChatbotAsync();
                _isWaitingForResponse = false;
                if (response == null)
                {
                    Environment.PrintError("Failed to create Chat API. API key not found.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _isWaitingForResponse = false;
                Environment.PrintError(ex.Message);
                return string.Empty;
            }

            return response;
        }

        public async Task<bool> CreateImageAsync(string resourceKey, string prompt)
        {
            Guard.IsNotNull(_api);

            // Wait for the previous response to be received
            while (_isWaitingForResponse)
            {
                await Task.Delay(100);
            }

            try
            {
                var pathResult = ResourceUtils.GetResourcePath(resourceKey, Environment.ProjectFolder);
                if (pathResult is ErrorResult<string> errorResult)
                {
                    Environment.PrintError("Failed to create image. Invalid resource key.");
                    return false;
                }
                var downloadPath = pathResult.Data;
                Guard.IsNotNull(downloadPath);

                _isWaitingForResponse = true;

                var request = new ImageGenerationRequest(prompt, 1, ImageSize._1024);

                var result = await _api.ImageGenerations.CreateImageAsync(request);

                _isWaitingForResponse = false;
                if (result == null || result.Data.Count == 0)
                {
                    Environment.PrintError("Failed to create image. API key not found.");
                    return false;
                }

                var imageUrl = result.Data[0].Url;

                var downloadResult = await DownloadImageAsync(imageUrl, downloadPath);
                if (downloadResult is ErrorResult downloadError)
                {
                    Environment.PrintError(downloadError.Message);
                }
            }
            catch (Exception ex)
            {
                _isWaitingForResponse = false;
                Environment.PrintError(ex.Message);
                return false;
            }

            return true;
        }

        public static async Task<Result> DownloadImageAsync(string imageUrl, string savePath)
        {
            try
            {
                var client = new HttpClient();

                // Send asynchronous GET request to the specified URL
                HttpResponseMessage response = await client.GetAsync(imageUrl);

                // Ensure we received a successful response
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Error: " + response.StatusCode);
                }

                // Read the response content as a byte array
                byte[] imageData = await response.Content.ReadAsByteArrayAsync();

                var directory = Path.GetDirectoryName(savePath);
                Guard.IsNotNull(directory);
                Directory.CreateDirectory(directory);

                // Write the image byte array to a file
                await File.WriteAllBytesAsync(savePath, imageData);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to download image. {ex.Message}");
            }
        }
    }
}
