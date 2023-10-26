using System.Threading.Tasks;

namespace CelRuntime.Interfaces
{
    public interface IChatService
    {
        public bool StartChat(string context);
        Task<string> Ask(string question);
        public void EndChat();
    }
}
