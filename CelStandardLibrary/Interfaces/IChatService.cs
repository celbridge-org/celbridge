using System.Threading.Tasks;

namespace CelStandardLibrary.Interfaces
{
    public interface IChatService
    {
        public bool StartChat(string context);
        Task<string> Ask(string question);
        public void EndChat();
    }
}
