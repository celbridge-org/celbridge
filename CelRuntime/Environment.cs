using CelRuntime.Interfaces;
using System;
using System.IO;

namespace CelRuntime
{
    public static class Environment
    {
        public static string ProjectFolder { get; set; }
        public static string GetPath(string file)
        {
            return Path.Combine(ProjectFolder, file);
        }

        public static Action<string> OnPrint;
        public static void Print(object message)
        {
            var text = message.ToString();
            OnPrint?.Invoke(text);
        }
    }
}
