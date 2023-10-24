using CelRuntime.Interfaces;
using CliWrap;
using CliWrap.Buffered;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CelRuntime
{
    public static class Environment
    {
        public static string ProjectFolder { get; set; }

        public static Action<string> OnPrint;
        public static IChatService ChatService { get; set; }

        public static void Print(object message)
        {
            var text = message.ToString();
            OnPrint?.Invoke(text);
        }

        public static string GetPath(string file)
        {
            return Path.Combine(ProjectFolder, file);
        }
    }
}
