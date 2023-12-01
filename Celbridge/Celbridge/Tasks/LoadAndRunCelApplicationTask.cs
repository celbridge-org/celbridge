using System.Reflection;
using System.Runtime.Loader;

namespace Celbridge.Tasks
{
    public class LoadAndRunCelApplicationTask
    {
        private AssemblyLoadContext? _loadContext;

        public WeakReference? CelApplicationAssembly { get; private set; }
        public WeakReference? CelRuntimeAssembly { get; private set; }

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
                CelRuntimeAssembly = null;
            }

            return new SuccessResult();
        }

        public Result Load(string celApplicationAssemblyPath)
        {
            try
            {
                Guard.IsTrue(File.Exists(celApplicationAssemblyPath));

                Guard.IsNull(_loadContext);
                _loadContext = new AssemblyLoadContext("CelApplications", true);

                // Doing it this way prevents the assembly file from being locked by the file system
                // Todo: Generate and load debug symbols for the CelSignatures assembly
                byte[] dllBytes = File.ReadAllBytes(celApplicationAssemblyPath);
                //byte[] pdbBytes = File.ReadAllBytes("./Server.Hotfix.pdb");

                var celApplication = _loadContext.LoadFromStream(new MemoryStream(dllBytes));
                Guard.IsNotNull(celApplication);
                CelApplicationAssembly = new WeakReference(celApplication);

                // Load the Cel Standard Library assembly
                var assemblyName = typeof(CelRuntime.Environment).Assembly.GetName();
                var celRuntimeAssembly = _loadContext.LoadFromAssemblyName(assemblyName);
                Guard.IsNotNull(celRuntimeAssembly);
                CelRuntimeAssembly = new WeakReference(celRuntimeAssembly);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Error loading Signature Assembly: {ex.Message}");
            }
        }

        public async Task<Result> Run(string celScriptName, string celName, string projectFolder, Action<string> onPrint, string chatAPIKey, string sheetsAPIKey)
        {
            if (string.IsNullOrEmpty(celScriptName) || string.IsNullOrEmpty(celName))
            {
                return new ErrorResult($"Failed to play Cel, '{celScriptName}.{celName}'");
            }

            if (CelApplicationAssembly == null ||
                CelRuntimeAssembly == null)
            {
                return new ErrorResult("Failed to run cel application. Assembly is not loaded.");
            }

            var celRuntimeAssembly = CelRuntimeAssembly.Target as Assembly;
            Guard.IsNotNull(celRuntimeAssembly);

            // Initialize the Environment
            var environmentType = celRuntimeAssembly.GetType("CelRuntime.Environment");
            Guard.IsNotNull(environmentType);
            {
                var initMethod = environmentType.GetMethod("Init", BindingFlags.Static | BindingFlags.Public);
                Guard.IsNotNull(initMethod);

                var result = initMethod.Invoke(null, new object[]
                {
                    projectFolder,
                    onPrint,
                    chatAPIKey,
                    sheetsAPIKey
                });

                if ((bool?)result == false)
                {
                    return new ErrorResult("Failed to initialize cel application environment.");
                }
            }

            // Find every public Start() method

            var assembly = CelApplicationAssembly.Target as Assembly;
            if (assembly == null)
            {
                return new ErrorResult("Failed to run cel application. Assembly is not loaded.");
            }

            var type = assembly.GetType($"CelApplication.{celScriptName}");
            if (type == null)
            {
                return new ErrorResult($"Failed to play Cel '{celScriptName}.{celName}'. No matching Cel class found in application.");
            }

            var methodInfo = type.GetMethod(celName, BindingFlags.Static | BindingFlags.Public);
            if (methodInfo == null)
            {
                return new ErrorResult($"Failed to play Cel '{celScriptName}.{celName}'. No matching method found in Cel class.");
            }

            try
            {
                Log.Information($"> Playing `{celScriptName}.{celName}` at {DateTime.Now.ToString("HH:mm:ss")}");

                var task = (Task?)methodInfo.Invoke(null, null);
                if (task is not null)
                {
                    await task;
                }

                // Note: .NET doesn't supporting catch a StackOverflow exception, it will just kill the process.
                // The best way around this seems to be to compile an executable assembly and run it in a new process.
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return new ErrorResult($"error:{ex.ParamName}");
            }
            catch (Exception ex)
            {
                return new ErrorResult(ex.Message);
            }

            return new SuccessResult();
        }
    }
}
