using Celbridge.Utilities;
using System.Reflection;
using System.Text.Json;

namespace Celbridge.Entities;

/// <summary>
/// An abstract base class for implementing a component editor.
/// Handles the default binding for viewing and updating component properties.
/// Override the virtual methods to customize the behaviour of the editor.
/// </summary>
public abstract class ComponentEditorBase : IComponentEditor
{
    private IComponentEditorHelper? _helper;

    public Guid EditorId { get; } = Guid.NewGuid();

    public event Action<string>? FormPropertyChanged;

    private IComponentProxy? _component;
    public IComponentProxy Component => _component!;

    public virtual Result Initialize(IComponentProxy component)
    {
        _component = component;
        return Result.Ok();
    }

    public abstract string GetComponentConfig();

    public abstract string GetComponentForm();

    public virtual string GetComponentRootForm()
    {
        return string.Empty;
    }

    public abstract ComponentSummary GetComponentSummary();

    public virtual void OnFormLoaded()
    {
        if (_helper is not null)
        {
            // If the _helper is already created then just return.
            // OnFormLoaded() may be called multiple times. For example a root component
            // may have botth a root form and a detail form, which will both call
            // OnFormLoaded(). Both forms use the same component editor and helper instances. 
            return;
        }

        _helper = ServiceLocator.AcquireService<IComponentEditorHelper>();
        _helper.Initialize(Component);
        _helper.ComponentPropertyChanged += OnComponentPropertyChanged;
    }

    public virtual void OnFormUnloaded()
    {
        if (_helper is null)
        {
            // OnFormUnloaded() can be called multiple times. Noop if already unloaded.
            return;
        }

        _helper.ComponentPropertyChanged -= OnComponentPropertyChanged;
        _helper.Uninitialize();
        _helper = null;
    }

    public Result<string> GetProperty(string propertyPath)
    {
        var getOverrideResult = TryGetProperty(propertyPath);
        if (getOverrideResult.IsSuccess)
        {
            // Derived class has intercepted this property
            return getOverrideResult;
        }

        // Try to get the property via the component system
        var getComponentResult = Component.GetProperty(propertyPath);

        if (getComponentResult.IsFailure)
        {
            // Use reflection to check if the derived ComponentEditor class exposes a matching public getter.
            var getEditorResult = GetComponentEditorProperty(propertyPath);
            if (getEditorResult.IsSuccess)
            {
                var json = getEditorResult.Value;
                return Result<string>.Ok(json);
            }
        }

        return getComponentResult;
    }

    public Result SetProperty(string propertyPath, string jsonValue, bool insert = false)
    {
        var setOverrideResult = TrySetProperty(propertyPath, jsonValue);
        if (setOverrideResult.IsSuccess)
        {
            // The derived class has intercepted this property get
            return Result.Ok();
        }

        // Set the property on the component
        var setComponentResult = Component.SetProperty(propertyPath, jsonValue, insert);

        if (setComponentResult.IsFailure)
        {
            // Use reflection to check if the derived ComponentEditor class exposes a matching public setter.
            var setEditorResult = SetComponentEditorProperty(propertyPath, jsonValue);
            if (setEditorResult.IsSuccess)
            {
                var changed = setEditorResult.Value;
                if (changed)
                {
                    NotifyFormPropertyChanged(propertyPath);
                }

                return Result.Ok();
            }
        }

        return setComponentResult;
    }

    protected virtual Result<string> TryGetProperty(string propertyPath)
    {
        return Result<string>.Fail();
    }

    protected virtual Result TrySetProperty(string propertyPath, string jsonValue)
    {
        return Result.Fail();
    }

    public virtual void OnButtonClicked(string buttonId)
    {}

    /// <summary>
    /// Loads a text file from an embedded resource.
    /// </summary>
    protected string LoadEmbeddedResource(string resourcePath)
    {
        var utilityService = ServiceLocator.AcquireService<IUtilityService>();

        // Get the type of the component editor class.
        // The embedded resource must be in the same assembly as the class that inherits
        // from ComponentEditorBase.
        var loadResult = utilityService.LoadEmbeddedResource(GetType(), resourcePath);
        if (loadResult.IsFailure)
        {
            return string.Empty;
        }
        var content = loadResult.Value;

        return content;
    }

    /// <summary>
    /// Notify the form that a property has changed.
    /// This is generally used for updating "virtual" properties that are not stored in 
    /// the component.
    /// </summary>
    protected virtual void NotifyFormPropertyChanged(string propertyPath)
    {
        FormPropertyChanged?.Invoke(propertyPath);
        OnFormPropertyChanged(propertyPath);
    }

    /// <summary>
    /// Event handler called when a form property has changed
    /// </summary>
    protected virtual void OnFormPropertyChanged(string propertyPath)
    {}

    private void OnComponentPropertyChanged(string propertyPath)
    {
        // Forward component property changes to the form
        NotifyFormPropertyChanged(propertyPath);
    }

    private Result<string> GetComponentEditorProperty(string propertyPath)
    {
        try
        {
            PropertyInfo? propertyInfo = GetPropertyInfo(propertyPath);
            if (propertyInfo is null)
            {
                return Result<string>.Fail();
            }

            var value = propertyInfo.GetValue(this);
            if (value is null)
            {
                return Result<string>.Fail();
            }

            var json = JsonSerializer.Serialize(value);
            return Result<string>.Ok(json);
        }
        catch
        {
            return Result<string>.Fail();
        }
    }

    private Result<bool> SetComponentEditorProperty(string propertyPath, string jsonValue)
    {
        try
        {
            PropertyInfo? propertyInfo = GetPropertyInfo(propertyPath);
            if (propertyInfo is null)
            {
                return Result<bool>.Fail();
            }

            if (propertyInfo.CanRead)
            {
                var value = propertyInfo.GetValue(this);
                if (value is null)
                {
                    return Result<bool>.Fail();
                }

                var currentValue = JsonSerializer.Serialize(value);
                if (jsonValue == currentValue)
                {
                    // Value has not changed
                    return Result<bool>.Ok(false);
                }
            }

            // Set the new value

            var deserialized = JsonSerializer.Deserialize(jsonValue, propertyInfo.PropertyType);
            propertyInfo.SetValue(this, deserialized, null);

            // Value has changed
            return Result<bool>.Ok(true);
        }
        catch
        {
            return Result<bool>.Fail();
        }
    }

    private PropertyInfo? GetPropertyInfo(string propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath) || !propertyPath.StartsWith("/"))
        {
            return null;
        }

        // Convert propertyPath to PascalCase with no leading slash
        var name = propertyPath.Substring(1);
        var propertyName = char.ToUpperInvariant(name[0]) + name.Substring(1);

        // Search for a matching property on this class (case sensitive)
        var ownerType = this.GetType();
        var properties = ownerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyInfo = properties.FirstOrDefault(prop =>
            Attribute.IsDefined(prop, typeof(ComponentPropertyAttribute)) &&
             string.Equals(propertyName, prop.Name, StringComparison.Ordinal)
        );

        return propertyInfo;
    }
}
