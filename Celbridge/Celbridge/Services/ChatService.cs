using Celbridge.Utils;
using CelLegacy.OpenAI;
using OpenAI_API;
using OpenAI_API.Chat;

namespace Celbridge.Services
{
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

        private bool InitChatAPI()
        {
            if (_api is not null)
            {
                return true;
            }

            Guard.IsNotNull(_settingsService.EditorSettings);
            var apiKey = _settingsService.EditorSettings.OpenAIKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Error("Failed to create Chat API. API key not found.");
                return false;
            }

            // https://github.com/OkGoDoIt/OpenAI-API-dotnet
            _api = new OpenAIAPI(apiKey);
            if (_api is null)
            {
                Log.Error("Failed to create Chat API");
                return false;
            }

            return true;
        }

        public bool StartChat(string context)
        {
            if (!InitChatAPI())
            {
                return false;
            }
            Guard.IsNotNull(_api);

            _chat = _api.Chat.CreateConversation();
            if (_chat == null)
            {
                Log.Error("Failed to create Chat AI conversation");
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
                    Log.Error("Failed to get response from Chat AI");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _isWaitingForResponse = false;
                Log.Error(ex.Message);
                return string.Empty;
            }

            return response;
        }

        public async Task<Result> TextToSpeech(string text)
        {
            Guard.IsNotNull(_settingsService.EditorSettings);
            var apiKey = _settingsService.EditorSettings.OpenAIKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                return new ErrorResult("API key not found.");
            }

            try
            {
                var openAI = new OpenAIUtilities();
                var byteData = await openAI.ConvertTextToSpeechAsync(apiKey, text);
                if (byteData == null)
                {
                    return new ErrorResult<byte[]>("Failed to convert text to speech");
                }

                var writeResult = await MediaUtils.WriteMediaFile(byteData, "textToSpeech.mp3");
                if (writeResult is ErrorResult<string> writeError)
                {
                    return new ErrorResult($"TextToVoice failed. {writeError.Message}");
                }
                var tempFile = writeResult.Data;
                Guard.IsNotNull(tempFile);

                await MediaUtils.OpenMediaFile(tempFile);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to convert text to speech. {ex.Message}");
            }
        }
    }
}
