#nullable enable

using CommunityToolkit.Diagnostics;
using OpenAI_API;
using OpenAI_API.Chat;
using System;
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
    }
}
