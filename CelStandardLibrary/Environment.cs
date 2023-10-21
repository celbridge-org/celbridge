using CelStandardLibrary.Interfaces;
using CliWrap;
using CliWrap.Buffered;
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

        public static void Print(object message)
        {
            var text = message.ToString();
            OnPrint?.Invoke(text);
        }

        public static string GetPath(string file)
        {
            return Path.Combine(ProjectFolder, file);
        }

        public static async Task NoOpAsync()
        {
            await Task.CompletedTask;
        }

        public static async Task<string> StartProcess(string target, string arguments)
        {
            try
            {
                if (target.EndsWith(".ps1"))
                {
                    // Execute a powershell script
                    var args = new string[] { target, arguments };
                    var result = await Cli.Wrap("powershell")
                        .WithArguments(args)
                        .WithWorkingDirectory(ProjectFolder)
                        .ExecuteBufferedAsync();

                    if (result.ExitCode != 0)
                    {
                        OnPrint?.Invoke($"Error: {result.StandardError}");
                    }

                    return result.StandardOutput;
                }
                else
                {
                    // Execute a regular command line tool
                    var result = await Cli.Wrap(target)
                        .WithArguments(arguments)
                        .WithWorkingDirectory(ProjectFolder)
                        .ExecuteBufferedAsync();

                    if (result.ExitCode != 0)
                    {
                        OnPrint?.Invoke($"Error: {result.StandardError}");
                    }

                    return result.StandardOutput;
                }
            }
            catch (Exception ex)
            {
                OnPrint?.Invoke(ex.Message);
            }
            return string.Empty;
        }
    }
}
