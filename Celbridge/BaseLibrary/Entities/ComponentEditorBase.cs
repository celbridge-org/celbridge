using Celbridge.Utilities;

namespace Celbridge.Entities;

/// <summary>
/// An abstract base class for implementing a component editor.
/// Handles the default binding for viewing and updating component properties.
/// Override the virtual methods to customize the behaviour of the editor.
/// </summary>
public abstract class ComponentEditorBase : IComponentEditor
{
    private IComponentEditorHelper? _helper;

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

    public abstract ComponentSummary GetComponentSummary();

    public virtual void OnFormLoaded()
    {
        Guard.IsNull(_helper);

        _helper = ServiceLocator.AcquireService<IComponentEditorHelper>();
        _helper.Initialize(Component);
        _helper.ComponentPropertyChanged += OnComponentPropertyChanged;
    }

    public virtual void OnFormUnloaded()
    {
        if (_helper is null)
        {
            // Unload can be called multiple times, noop if already unloaded
            return;
        }

        _helper.ComponentPropertyChanged -= OnComponentPropertyChanged;
        _helper.Uninitialize();
        _helper = null;
    }

    public Result<string> GetProperty(string propertyPath)
    {
        var getResult = TryGetProperty(propertyPath);
        if (getResult.IsSuccess)
        {
            // Derived class has intercepted this property get
            return getResult;
        }

        // Get the property from the component
        return Component.GetProperty(propertyPath);
    }

    public Result SetProperty(string propertyPath, string jsonValue, bool insert = false)
    {
        var setResult = TrySetProperty(propertyPath, jsonValue);
        if (setResult.IsSuccess)
        {
            // The derived class has intercepted this property get
            return Result.Ok();
        }

        // Set the property on the component
        return Component.SetProperty(propertyPath, jsonValue, insert);
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
}
