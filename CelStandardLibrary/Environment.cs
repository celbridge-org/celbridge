using CelStandardLibrary.Interfaces;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CelStandardLibrary
{
    public static class Environment
    {
        public static string ProjectFolder { get; set; }

        public static Action<string> OnPrint;
        public static IChatService ChatService { get; set; }

        public static void Print(string message)
        {
            OnPrint?.Invoke(message);
        }

        public static string GetPath(string file)
        {
            return Path.Combine(ProjectFolder, file);
        }

        public static async Task NoOpAsync()
        {
            await Task.CompletedTask;
        }
    }
}
