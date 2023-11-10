using System;
using System.IO;

namespace CelRuntime
{
    public static class Environment
    {
        public static string ProjectFolder { get; private set; }

        private static Action<string> _onPrint;
        public static void Print(object message)
        {
            var text = message.ToString();
            _onPrint?.Invoke(text);
        }
        public static void PrintError(object message)
        {
            var text = message.ToString();
            _onPrint?.Invoke($"Error: {text}");
        }

        public static TextFile TextFile { get; private set; }
        public static Process Process { get; private set; }
        public static Chat Chat { get; private set; }
        public static Markdown Markdown { get; private set; }

        public static bool Init(string projectFolder, Action<string> onPrint, string chatAPIKey)
        {
            if (string.IsNullOrEmpty(projectFolder))
            {
                PrintError("Project folder not specified");
                return false;
            }
            ProjectFolder = projectFolder;

            if (onPrint is null)
            {
                PrintError("onPrint callback not specified");
                return false;
            }
            _onPrint = onPrint;

            TextFile = new TextFile();
            Process = new Process();

            if (string.IsNullOrEmpty(chatAPIKey))
            {
                PrintError("Chat API Key not specified");
                return false;
            }
            Chat = new Chat();
            if (!Chat.Init(chatAPIKey))
            {
                PrintError("Failed to init Chat API");
                return false;
            }
            Markdown = new Markdown();

            return true;
        }
    }
}
