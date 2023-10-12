using System;

namespace CelStandardLibrary
{
    public static class TextFile
    {
        public static string ReadText(string resource)
        {
            try
            {
                Environment.Print($"Read: {resource}");
                return "ok";
            }
            catch (Exception ex)
            {
                // Todo: Log errors using the Environment.Log thingy
            }

            return string.Empty;
        }
    }
}
