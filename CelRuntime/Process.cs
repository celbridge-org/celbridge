using System.Threading.Tasks;
using System;
using CliWrap;
using CliWrap.Buffered;

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
                    // Execute a regular command line tool
                    var result = await Cli.Wrap(target)
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
