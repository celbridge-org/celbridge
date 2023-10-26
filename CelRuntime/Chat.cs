#nullable enable

using CommunityToolkit.Diagnostics;
using OpenAI_API;
using OpenAI_API.Chat;
using System;
using System.Threading.Tasks;

namespace CelRuntime
{
    public static class Chat
    {
        private static OpenAIAPI? _api;
        private static Conversation? _chat;

        private static bool _isWaitingForResponse;

        public static bool Init(string apiKey)
        {
            if (_api is not null)
            {
                return true;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Environment.Print("Error: Failed to create Chat API. API key not found.");
                return false;
            }

            // https://github.com/OkGoDoIt/OpenAI-API-dotnet
            _api = new OpenAIAPI(apiKey);
            if (_api is null)
            {
                Environment.Print("Error: Failed to create Chat API. API key not found.");
                return false;
            }

            return true;
        }

        public static bool StartChat(string context)
        {
            Guard.IsNotNull(_api);

            _chat = _api.Chat.CreateConversation();
            if (_chat == null)
            {
                Environment.Print("Error: Failed to create Chat API. API key not found.");
                return false;
            }

            _chat.AppendSystemMessage(context);

            return true;
        }

        public static void EndChat()
        {
            _chat = null;
        }

        public static async Task<string> Ask(string question)
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
                    Environment.Print("Error: Failed to create Chat API. API key not found.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _isWaitingForResponse = false;
                Environment.Print($"Error: {ex.Message}");
                return string.Empty;
            }

            return response;
        }
    }
}
