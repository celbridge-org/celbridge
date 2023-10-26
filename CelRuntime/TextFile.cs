using System;
using System.IO;

namespace CelRuntime
{
    public class TextFile
    {
        public string ReadText(string resourceFile)
        {
            try
            {
                var path = Environment.GetPath(resourceFile);
                var text = File.ReadAllText(path);
                return text;
            }
            catch (Exception ex)
            {
                Environment.PrintError(ex.ToString());
            }

            return string.Empty;
        }

        public void WriteText(string resourceFile, string text)
        {
            try
            {
                var path = Environment.GetPath(resourceFile);
                File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                // Todo: Log errors using the Environment.Log thingy
                Environment.PrintError(ex.ToString());
            }
        }
    }
}
