using System.Threading.Tasks;
using System;
using CliWrap;
using CliWrap.Buffered;
using System.IO;

namespace CelRuntime
{
    public class Process
    {
        public async Task<string> StartProcess(string target, string arguments)
        {
            try
            {
                if (target.EndsWith(".ps1"))
                {
                    // Execute a powershell script
                    var args = new string[] { target, arguments };
                    var result = await Cli.Wrap("powershell")
                        .WithArguments(args)
                        .WithWorkingDirectory(Environment.ProjectFolder)
                        .ExecuteBufferedAsync();

                    if (result.ExitCode != 0)
                    {
                        Environment.PrintError(result.StandardError);
                    }

                    return result.StandardOutput;
                }
                else
                {

                    string targetPath = target;
                    if (target.StartsWith("/"))
                    {
                        // Target is an executable in the project
                        targetPath = Path.Combine(Environment.ProjectFolder, target.Substring(1));
                        targetPath = Path.GetFullPath(targetPath);
                    }

                    // Execute a regular command line tool
                    var result = await Cli.Wrap(targetPath)
                        .WithArguments(arguments)
                        .WithWorkingDirectory(Environment.ProjectFolder)
                        .ExecuteBufferedAsync();

                    if (result.ExitCode != 0)
                    {
                        Environment.PrintError(result.StandardError);
                    }

                    return result.StandardOutput;
                }
            }
            catch (Exception ex)
            {
                Environment.Print(ex.Message);
            }
            return string.Empty;
        }
    }
}
