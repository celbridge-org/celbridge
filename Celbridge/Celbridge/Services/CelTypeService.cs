using Celbridge.Models;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celbridge.Services
{
    public interface ICelTypeService
    {
        List<string> CelTypeNames { get; }

        Result<ICelType> GetCelType(string name);
        Result<ICelType> GetCelType(Type type);
    }

     public class CelTypeService : ICelTypeService
    {
        public List<string> CelTypeNames { get; } = new();

        private readonly Dictionary<string, ICelType> _celTypesByName = new();
        private readonly Dictionary<Type, ICelType> _celTypesByType = new();

        public CelTypeService()
        {
            try
            {
                // Get all the types in the current assembly
                Type[] allTypes = Assembly.GetExecutingAssembly().GetTypes();

                // Filter types that implement the ICelType interface
                Type[] types = allTypes
                    .Where(type => typeof(ICelType).IsAssignableFrom(type) && !type.IsInterface)
                    .ToArray();

                foreach (var type in types)
                {
                    var celType = Activator.CreateInstance(type) as ICelType;
                    Guard.IsNotNull(celType);

                    var celTypeName = celType.Name;
                    if (CelTypeNames.Contains(celTypeName))
                    {
                        Log.Error($"CelType names must be unique {celTypeName}.");
                        continue;
                    }

                    CelTypeNames.Add(celType.Name);

                    _celTypesByName[celTypeName] = celType;
                    _celTypesByType[type] = celType;
                }

                CelTypeNames.Sort();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to register Cel Types: {ex.Message}");
            }
        }

        public Result<ICelType> GetCelType(string name)
        {
            if (_celTypesByName.TryGetValue(name, out var celType))
            {
                return new SuccessResult<ICelType>(celType);
            }
            return new ErrorResult<ICelType>($"Failed to get Cel Type by name '{name}'.");
        }

        public Result<ICelType> GetCelType(Type type)
        {
            if (_celTypesByType.TryGetValue(type, out var celType))
            {
                return new SuccessResult<ICelType>(celType);
            }
            return new ErrorResult<ICelType>($"Failed to get Cel Type by type '{type}'.");
        }
    }
}