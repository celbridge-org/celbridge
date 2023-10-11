using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using OpenAI_API;
using OpenAI_API.Chat;
using Serilog;

namespace Celbridge.Services
{
    public interface IAIService
    {
        public Result StartChat();
        public Result EndChat();

        Task<Result<string>> AddUserInput(string question);
    }

    public class AIService : IAIService
    {
        private OpenAIAPI? _api;
        private Conversation? _chat;

        private bool _isWaitingForResponse;

        public AIService()
        {
            // https://github.com/OkGoDoIt/OpenAI-API-dotnet
            InitChatAPI();
        }

        private Result InitChatAPI()
        {
            _api = new OpenAIAPI("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");
            if (_api == null)
            {
                return new ErrorResult("Failed to create Chat AI API");
            }

            return new SuccessResult();
        }

        public Result StartChat()
        {
            Guard.IsNotNull(_api);

            _chat = _api.Chat.CreateConversation();
            if (_chat == null)
            {
                return new ErrorResult("Failed to create Chat AI conversation");
            }

            return new SuccessResult();
        }

        public Result EndChat()
        {
            _chat = null;
            return new SuccessResult();
        }

        public async Task<Result<string>> AddUserInput(string userInput)
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

                _chat.AppendUserInput(userInput);
                _isWaitingForResponse = true;
                response = await _chat.GetResponseFromChatbotAsync();
                _isWaitingForResponse = false;
                if (response == null)
                {
                    return new ErrorResult<string>("Failed to get response from Chat AI");
                }
            }
            catch (Exception ex)
            {
                _isWaitingForResponse = false;
                Log.Error(ex.Message);
                return new ErrorResult<string>(ex.Message);
            }

            return new SuccessResult<string>(response);
        }
    }
}
