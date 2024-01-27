namespace Celbridge.Legacy.Services;

public interface IChatService
{
    public bool StartChat(string context);
    Task<string> Ask(string question);
    public void EndChat();
    public Task<Result> TextToSpeech(string text);
}
