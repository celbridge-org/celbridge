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
    public static class JsonSettings
    {
        public static JsonSerializerSettings Create()
        {
            void OnError(object? sender, Newtonsoft.Json.Serialization.ErrorEventArgs e)
            {
                e.ErrorContext.Handled = true;
            }

            var settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Formatting = Formatting.Indented,
                Error = OnError,
                ContractResolver = new DefaultContractResolver(), // This should only cache types for this instance of the settings
            };

            return settings;
        }
    }
}
