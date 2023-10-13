using System;
using System.IO;

namespace CelStandardLibrary
{
    public static class Environment
    {
        public static string ProjectFolder { get; set; }

        public static Action<string> OnPrint;

        public static void Print(string message)
        {
            OnPrint?.Invoke(message);
        }

        public static string GetPath(string file)
        {
            return Path.Combine(ProjectFolder, file);
        }
    }
}
