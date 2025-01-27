namespace Celbridge.Entities;

/// <summary>
/// An abstract base class for implementing a component editor.
/// Handles the default binding for viewing and updating component properties.
/// Override the virtual methods to customize the behaviour of the editor.
/// </summary>
public abstract class ComponentEditorBase : IComponentEditor
{
    public abstract string ComponentConfigPath { get; }

    public event Action<string>? FormPropertyChanged;

    protected IComponentProxy? _component;
    public IComponentProxy Component => _component!;

    public virtual Result Initialize(IComponentProxy component)
    {
        _component = component;
        _component.ComponentPropertyChanged += OnComponentPropertyChanged;

        return Result.Ok();
    }

    public abstract Result<ComponentSummary> GetComponentSummary();

    public virtual void OnFormUnloaded()
    {
        if (_component is not null)
        {
            _component.ComponentPropertyChanged -= OnComponentPropertyChanged;
        }
    }

    public virtual Result<string> GetProperty(string propertyPath)
    {
        if (_component is null)
        {
            return Result<string>.Fail("Component is null");
        }

        var value = Component.GetString(propertyPath);

        return Result<string>.Ok(value);
    }

    public virtual Result SetProperty(string propertyPath, string newValue, bool insert)
    {
        if (_component is null)
        {
            return Result<string>.Fail("Component is null");
        }

        var setResult = Component.SetProperty(propertyPath, newValue);
        if (setResult.IsFailure)
        {
            return Result.Fail($"SetProperty failed: {propertyPath}")
                .WithErrors(setResult);
        }

        return Result.Ok();
    }

    protected virtual void OnComponentPropertyChanged(string propertyPath)
    {
        // Forward the property changed event so the editor view can update itself
        FormPropertyChanged?.Invoke(propertyPath);
    }
}
