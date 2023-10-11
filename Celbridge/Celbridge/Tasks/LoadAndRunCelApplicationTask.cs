using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Celbridge.Tasks
{
    public class LoadAndRunCelApplicationTask
    {
        private AssemblyLoadContext? _loadContext;

        public WeakReference? CelApplicationAssembly { get; private set; }

        public Result Unload()
        {
            if (_loadContext != null)
            {
                _loadContext.Unload();
                _loadContext = null;

                Guard.IsNotNull(CelApplicationAssembly);

                for (int i = 0; CelApplicationAssembly.IsAlive && (i < 10); i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

#if !DEBUG
                // In release builds the assembly should be completely unloaded from memory.
                // In debug builds, the Visual Studio debugger keeps a reference to the assembly
                // which prevents it from being garbage collected. This causes a small
                // memory leak whenever you refresh the project while debugging but shouldn't
                // have any other negative effect.

                var assembly = CelApplicationAssembly.Target as Assembly;
                bool isAlive = assembly != null;
                Log.Information($"Cel Application Assembly is alive: {isAlive}");
#endif

                CelApplicationAssembly = null;
            }

            return new SuccessResult();
        }

        public Result Load(string celApplicationAssembly)
        {
            try
            {
                Guard.IsTrue(File.Exists(celApplicationAssembly));

                Guard.IsNull(_loadContext);
                _loadContext = new AssemblyLoadContext("CelApplications", true);

                // Doing it this way prevents the assembly file from being locked by the file system
                // Todo: Generate and load debug symbols for the CelSignatures assembly
                byte[] dllBytes = File.ReadAllBytes(celApplicationAssembly);
                //byte[] pdbBytes = File.ReadAllBytes("./Server.Hotfix.pdb");

                var assembly = _loadContext.LoadFromStream(new MemoryStream(dllBytes));
                Guard.IsNotNull(assembly);

                CelApplicationAssembly = new WeakReference(assembly);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Error loading Signature Assembly: {ex.Message}");
            }
        }

        public Result Run(Action<string> onPrint)
        {
            if (CelApplicationAssembly == null)
            {
                return new ErrorResult("Failed to run cel application. Assembly is not loaded.");
            }

            var assembly = CelApplicationAssembly.Target as Assembly;
            if (assembly == null)
            {
                return new ErrorResult("Failed to run cel application. Assembly is not loaded.");                
            }

            var environmentType = assembly.GetType("CelApplication.Environment");
            Guard.IsNotNull(environmentType);

            // Inject the print delegate
            var onPrintProperty = environmentType.GetField("OnPrint", BindingFlags.Static | BindingFlags.Public);
            Guard.IsNotNull(onPrintProperty);
            var printDelegate = new Action<string>(onPrint);
            onPrintProperty.SetValue(null, printDelegate);

            // Find every public Start() method
            var methods = assembly.GetTypes()
                .Where(type => type.IsClass && type.IsAbstract && type.IsSealed) // Filter static classes
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method =>
                    method.Name == "Start" &&                   // Method name is "Start"
                    method.GetParameters().Length == 0);        // No parameters

            try
            {
                // Execute each start method

                // Note: .NET doesn't supporting catch a StackOverflow exception, it will just kill the process.
                // The best way around this seems to be to compile an executable assembly and run it in a new process.

                foreach (var method in methods)
                {
                    method.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                return new ErrorResult(ex.Message);
            }

            return new SuccessResult();
        }
    }
}
