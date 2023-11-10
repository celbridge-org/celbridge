using System.Threading.Tasks;
using System;
using CliWrap;
using CliWrap.Buffered;
using System.IO;
using System.Text.RegularExpressions;
using CelUtilities.Resources;
using CelUtilities.ErrorHandling;

namespace CelRuntime
{
    public class Process
    {
        public async Task<string> StartProcess(string target, string arguments)
        {
            target = target.Trim();
            string expandedTarget = target;
            if (target.StartsWith("@"))
            {
                var targetResult = ResourceUtils.GetResourcePath(target, Environment.ProjectFolder);
                if (targetResult is ErrorResult<string> targetError)
                {
                    Environment.PrintError(targetError.Message);
                    return string.Empty;
                }
                expandedTarget = targetResult.Data;
            }

            var argumentsResult = ResourceUtils.ExpandResourceKeys(arguments, Environment.ProjectFolder);
            if (argumentsResult is ErrorResult<string> argumentsError)
            {
                Environment.PrintError(argumentsError.Message);
                return string.Empty;
            }
            var expandedArguments = argumentsResult.Data;

            try
            {
                if (expandedTarget.EndsWith(".ps1"))
                {
                    // Execute a powershell script
                    var args = new string[] { expandedTarget, expandedArguments };
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
                    var result = await Cli.Wrap(expandedTarget)
                        .WithArguments(expandedArguments)
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
                Environment.PrintError(ex.Message);
            }
            return string.Empty;
        }
    }
}
