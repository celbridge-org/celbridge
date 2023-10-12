using System;

namespace CelStandardLibrary
{
    public static class Environment
    {
        public static Action<string> OnPrint;

        public static void Print(string message)
        {
            OnPrint?.Invoke(message);
        }
    }
}
