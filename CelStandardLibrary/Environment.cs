using CelStandardLibrary.Interfaces;
using CliWrap;
using CliWrap.Buffered;
using System;
using System.Collections.Generic;
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

        public static async Task<string> StartProcess(string executable, string arguments)
        {
            try
            {
                var result = await Cli.Wrap(executable)
                    .WithArguments(arguments)
                    .WithWorkingDirectory(ProjectFolder)
                    .ExecuteBufferedAsync();

                if (result.ExitCode != 0)
                {
                    OnPrint?.Invoke($"Error: {result.StandardError}");
                }

                OnPrint?.Invoke(result.StandardOutput);

                return result.StandardOutput;
            }
            catch (Exception ex)
            {
                OnPrint?.Invoke(ex.Message);
            }
            return string.Empty;
        }
    }
}
