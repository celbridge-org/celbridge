using Celbridge.Tasks;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Reflection;

namespace Celbridge.Utils
{
    public static class CelScriptJsonSettings
    {
        public static JsonSerializerSettings Create()
        {
            void OnError(object sender, Newtonsoft.Json.Serialization.ErrorEventArgs e)
            {
                // Todo: Attempt to remap missing ICelSignature classes
                e.ErrorContext.Handled = true;
            }

            var settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented,
                Error = OnError,
                SerializationBinder = new CelSerializationBinder(),
                ContractResolver = new DefaultContractResolver(), // This should only cache types for this instance of the settings
            };

            return settings;
        }
    }

    public class CelSerializationBinder : DefaultSerializationBinder
    {
        private WeakReference _celSignatureAssembly;

        public override Type BindToType(string assemblyName, string typeName)
        {
            if (assemblyName is null || !assemblyName.Contains("CelSignatures.dll"))
            {
                return base.BindToType(assemblyName, typeName);
            }

            if (_celSignatureAssembly is null)
            {
                var services = (Application.Current as App).Host.Services;
                var loadCustomAssembliesTask = services.GetRequiredService<LoadCustomAssembliesTask>();
                var assembly = loadCustomAssembliesTask.CelSignatureAssembly.Target as Assembly;

                _celSignatureAssembly = new WeakReference(assembly);
            }

            var celSignatureAssembly = _celSignatureAssembly.Target as Assembly;
            Guard.IsNotNull(celSignatureAssembly);

            var celSignatureType = celSignatureAssembly.GetType(typeName);
            return celSignatureType;
        }
    }
}
