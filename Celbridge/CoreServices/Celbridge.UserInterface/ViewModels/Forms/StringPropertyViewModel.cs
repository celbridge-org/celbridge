using System.ComponentModel;
using Celbridge.Forms;

namespace Celbridge.UserInterface.ViewModels.Forms;

/// <summary>
/// Binds a UI element to a string property accessed via a form data provider.
/// </summary>
public partial class StringPropertyViewModel : ObservableObject, IPropertyViewModel
{
    [ObservableProperty]
    private string _value = string.Empty;

    public string BoundPropertyName => nameof(Value);

    private IFormDataProvider? _formDataProvider;
    private string _propertyPath = string.Empty;

    public Result Initialize(IFormDataProvider formDataProvider, string propertyPath)
    {
        _formDataProvider = formDataProvider;
        _propertyPath = propertyPath;

        // Read the current property value from the component
        var updateResult = UpdateValue();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize value: '{propertyPath}'")
                .WithErrors(updateResult);
        }

        // Listen for property changes on the component (via the form data provider)
        formDataProvider.FormPropertyChanged += OnFormDataPropertyChanged;

        // Listen for property changes on this view model (via ObservableObject)
        PropertyChanged += OnViewModelPropertyChanged;

        return Result.Ok();
    }

    public void OnViewUnloaded()
    {
        if (_formDataProvider != null)
        {
            // Unregister listeners
            _formDataProvider.FormPropertyChanged -= OnFormDataPropertyChanged;
            PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnFormDataPropertyChanged(string propertyPath)
    {
        if (propertyPath == _propertyPath)
        {
            UpdateValue();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Guard.IsNotNull(_formDataProvider);

        if (e.PropertyName == nameof(Value))
        {
            // Stop listening for component property changes while we update the component
            _formDataProvider.FormPropertyChanged -= OnFormDataPropertyChanged;

            _formDataProvider.SetProperty(_propertyPath, Value, false);

            // Start listening for component property changes again
            _formDataProvider.FormPropertyChanged += OnFormDataPropertyChanged;
        }
    }

    private Result UpdateValue()
    {
        Guard.IsNotNull(_formDataProvider);

        // Sync the value member variable with the property
        var getResult = _formDataProvider.GetProperty(_propertyPath);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get property: {_propertyPath}")
                .WithErrors(getResult);
        }

        Value = getResult.Value;

        return Result.Ok();
    }
}
