using System.Reflection;

namespace CelLegacy.Services;

public interface IResourceTypeService
{
    List<Type> ResourceTypes { get; }
    Result<ResourceTypeAttribute> GetResourceTypeInfo(Type type);
    public Result<Type> GetResourceTypeForExtension(string extension);
    public Result<Func<string, Result>> GetFactoryMethod(Type resourceType);
}

public class ResourceTypeService : IResourceTypeService
{
    private readonly Dictionary<Type, ResourceTypeAttribute> _resourceTypeInfo = new();
    private readonly Dictionary<string, Type> _extensionToResourceType = new();
    private readonly Dictionary<Type, Func<string, Result>> _resourceFactoryMethods = new();
    public List<Type> ResourceTypes { get; private set; } = new();

    public ResourceTypeService()
    {
        PopulateResourceTypes();
    }

    private void PopulateResourceTypes()
    {
        try
        {
            var resourceTypeClasses = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsDefined(typeof(ResourceTypeAttribute), false))
                .ToList();

            foreach (var resourceTypeClass in resourceTypeClasses)
            {
                if (resourceTypeClass.GetCustomAttributes(typeof(ResourceTypeAttribute), false)
                    .FirstOrDefault() is ResourceTypeAttribute attribute)
                {
                    // Store attribute for the ResourceType
                    _resourceTypeInfo.Add(resourceTypeClass, attribute);

                    if (resourceTypeClass != typeof(Project))
                    {
                        // Store factory method for the ResourceType
                        var factoryMethodInfo = resourceTypeClass.GetMethod("CreateResource", BindingFlags.Static | BindingFlags.Public);
                        Guard.IsNotNull(factoryMethodInfo);

                        Func<string, Result> factoryMethod = (Func<string, Result>)Delegate.CreateDelegate(typeof(Func<string, Result>), factoryMethodInfo);
                        if (factoryMethod == null)
                        {
                            throw new Exception($"Resource Type '{resourceTypeClass}' does not contain a valid factory method.");
                        }
                        _resourceFactoryMethods.Add(resourceTypeClass, factoryMethod);
                    }

                    foreach (var extension in attribute.Extensions)
                    {
                        if (_extensionToResourceType.ContainsKey(extension))
                        {
                            throw new Exception($"Failed to register extension '{extension}' for resource type '{resourceTypeClass}' because it has already been registered for another resource type.");
                        }
                        _extensionToResourceType.Add(extension, resourceTypeClass);
                    }
                }
            }

            resourceTypeClasses.Remove(typeof(Project));
            resourceTypeClasses.Sort((a, b) => a.Name.CompareTo(b.Name));

            ResourceTypes = resourceTypeClasses;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to register resource types: {ex.Message}");
        }
    }


    public Result<ResourceTypeAttribute> GetResourceTypeInfo(Type type)
    {
        if (_resourceTypeInfo.TryGetValue(type, out var typeInfo))
        {
            return new SuccessResult<ResourceTypeAttribute>(typeInfo);
        }
        return new ErrorResult<ResourceTypeAttribute>($"Failed to get Resource Type info for type '{type.Name}'.");
    }

    public Result<Type> GetResourceTypeForExtension(string extension)
    {
        if (_extensionToResourceType.TryGetValue(extension, out var resourceType))
        {
            return new SuccessResult<Type>(resourceType);
        }
        return new ErrorResult<Type>($"No resource type registered for extension '{extension}'");
    }

    public Result<Func<string, Result>> GetFactoryMethod(Type resourceType)
    {
        if (_resourceFactoryMethods.TryGetValue(resourceType, out var factoryMethod))
        {
            return new SuccessResult<Func<string, Result>>(factoryMethod);
        }
        return new ErrorResult<Func<string, Result>>($"Factory method not found for Resource Type '{resourceType}'");
    }
}
