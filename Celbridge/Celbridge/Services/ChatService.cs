using Celbridge.Utils;
using OpenAI_API;
using OpenAI_API.Chat;

namespace Celbridge.Services
{
    public interface IChatService
    {
        public Result StartChat();
        Task<Result<string>> Ask(string question);
        public Result EndChat();
    }

    public class ChatService : IChatService
    {
        private ISettingsService _settingsService;

        private OpenAIAPI? _api;
        private Conversation? _chat;

        private bool _isWaitingForResponse;

        public ChatService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        private Result InitChatAPI()
        {
            if (_api is not null)
            {
                return new SuccessResult();
            }

            Guard.IsNotNull(_settingsService.EditorSettings);
            var apiKey = _settingsService.EditorSettings.OpenAIKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                return new ErrorResult("Failed to create Chat API. API key not found.");
            }

            // https://github.com/OkGoDoIt/OpenAI-API-dotnet
            _api = new OpenAIAPI(apiKey);
            if (_api is null)
            {
                return new ErrorResult("Failed to create Chat AI API");
            }

            return new SuccessResult();
        }

        public Result StartChat()
        {
            var initResult = InitChatAPI();
            if (initResult is ErrorResult error)
            {
                return error;
            }
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

        public async Task<Result<string>> Ask(string question)
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
