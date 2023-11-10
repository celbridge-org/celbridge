using CelUtilities.ErrorHandling;
using CelUtilities.Resources;
using System;
using System.IO;

namespace CelRuntime
{
    public class TextFile
    {
        public string ReadText(string resourceKey)
        {
            try
            {
                var pathResult = ResourceUtils.GetResourcePath(resourceKey, Environment.ProjectFolder);
                if (pathResult is ErrorResult<string> pathError)
                {
                    Environment.PrintError(pathError.Message);
                    return string.Empty;
                }
                var path = pathResult.Data;

                var text = File.ReadAllText(path);
                return text;
            }
            catch (Exception ex)
            {
                Environment.PrintError(ex.ToString());
            }

            return string.Empty;
        }

        public void WriteText(string resourceKey, string text)
        {
            try
            {
                var pathResult = ResourceUtils.GetResourcePath(resourceKey, Environment.ProjectFolder);
                if (pathResult is ErrorResult<string> pathError)
                {
                    Environment.PrintError(pathError.Message);
                    return;
                }
                var path = pathResult.Data;

                var folder = Path.GetDirectoryName(path);
                Directory.CreateDirectory(folder);

                File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                Environment.PrintError(ex.ToString());
            }
        }
    }
}
