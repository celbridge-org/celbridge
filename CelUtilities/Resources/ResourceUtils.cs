using CelUtilities.ErrorHandling;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace CelUtilities.Resources
{
    public static class ResourceUtils
    {
        /// <summary>
        /// Returns an absolute path to a file in the project folder for the specified resourceKey.
        /// The resource key must start with:
        ///   '@' : Project folder resource
        ///   '#' : Relative or absolute path
        /// </summary>
        public static Result<string> GetResourcePath(string resourceKey, string projectFolder)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                return new ErrorResult<string>($"Failed to get resource path. Resource key is empty.");
            }

            string path = null;
            if (resourceKey.StartsWith("@"))
            {
                // Resource in the project folder, map it to an absolute path
                try
                {
                    var relativePath = resourceKey.Substring(1);
                    var combined = Path.Combine(projectFolder, relativePath);
                    path = Path.GetFullPath(combined);
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>($"Failed to get resource path. {ex}");
                }
            }
            else if (resourceKey.StartsWith("#"))
            {
                // Resource in the file system
                try
                {
                    path = resourceKey.Substring(1);
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>($"Failed to get resource path. {ex}");
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResult<string>($"Failed to get path for resource key: {resourceKey}");
            }

            return new SuccessResult<string>(path);
        }

        public static Result<string> ExpandResourceKeys(string input, string projectFolder)
        {
            if (input.IndexOf('@') == -1 && 
                input.IndexOf('#') == -1)
            {
                return new SuccessResult<string>(input);
            }

            // Input string
            // string inputString = "\"@Slides/Slide.md\" -o \"@Slides/Slides.html\" --theme gaia --allow-local-files";

            // Extract "@Some/Path" or "#Some/Path" parts from the string
            var matches = Regex.Matches(input, @"\""[@#].*?\"""); // Match quoted strings starting with @ or #

            // Replace '@' or '#' with 'Path/' and update the original string
            var output = input;
            foreach (Match match in matches)
            {
                string token = match.Value;
                string resourceKey = token.Trim('\"');

                var pathResult = GetResourcePath(resourceKey, projectFolder);
                if (pathResult is ErrorResult<string> pathError)
                {
                    return pathError;
                }
                var path = pathResult.Data;

                output = output.Replace(token, $"\"{path}\"");
            }

            return new SuccessResult<string>(output);
        }
    }
}
