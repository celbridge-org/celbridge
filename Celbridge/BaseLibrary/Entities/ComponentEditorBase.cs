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

    private void OnComponentPropertyChanged(string propertyPath)
    {
        // Forward component property changes to the form
        FormPropertyChanged?.Invoke(propertyPath);
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

    public string LoadEmbeddedResource(string resourcePath)
    {    
        var utilityService = ServiceLocator.AcquireService<IUtilityService>();
     
        // Get the type of the component editor class.
        // The embedded resource must be in the same assembly as this type.
        var loadResult = utilityService.LoadEmbeddedResource(GetType(), resourcePath);
        if (loadResult.IsFailure)
        {
            return string.Empty;
        }
        var content = loadResult.Value;

        return content;
    }

    public Result<string> GetProperty(string propertyPath)
    {
        // Todo: Add property override mechanism

        return Component.GetProperty(propertyPath);
    }

    public Result SetProperty(string propertyPath, string jsonValue, bool insert = false)
    {
        // Todo: Add property override mechanism

        return Component.SetProperty(propertyPath, jsonValue, insert);
    }

    public virtual void OnButtonClicked(string buttonId)
    {}
}
