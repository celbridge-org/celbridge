using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using System.Runtime.Loader;

namespace Celbridge.Tasks
{
    public class LoadCustomAssembliesTask
    {
        private AssemblyLoadContext? _loadContext;

        public WeakReference? CelSignatureAssembly { get; private set; }

        public Result Unload()
        {
            if (_loadContext != null)
            {
                _loadContext.Unload();
                _loadContext = null;

                Guard.IsNotNull(CelSignatureAssembly);

                for (int i = 0; CelSignatureAssembly.IsAlive && (i < 10); i++)
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

                var assembly = CelSignatureAssembly.Target as Assembly;
                bool isAlive = assembly != null;
                Log.Information($"Cel Signature Assembly is alive: {isAlive}");
#endif

                CelSignatureAssembly = null;
            }

            return new SuccessResult();
        }

        public Result Load(string celSignaturesAssembly)
        {
            try
            {
                Guard.IsTrue(File.Exists(celSignaturesAssembly));

                Guard.IsNull(_loadContext);
                _loadContext = new AssemblyLoadContext("CelbridgeAssemblies", true);

                // Doing it this way prevents the assembly file from being locked by the file system
                // Todo: Generate and load debug symbols for the CelSignatures assembly
                byte[] dllBytes = File.ReadAllBytes(celSignaturesAssembly);
                //byte[] pdbBytes = File.ReadAllBytes("./Server.Hotfix.pdb");

                var assembly = _loadContext.LoadFromStream(new MemoryStream(dllBytes));
                Guard.IsNotNull(assembly);

                CelSignatureAssembly = new WeakReference(assembly);

                return new SuccessResult();
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Error loading Signature Assembly: {ex.Message}");
            }
        }
    }
}
