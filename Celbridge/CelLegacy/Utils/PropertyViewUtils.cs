using System.ComponentModel;

namespace CelLegacy.Utils;

public static class PropertyViewUtils
{
    public static Result<List<UIElement>> CreatePropertyViews(object obj, PropertyContext context, PropertyChangedEventHandler onPropertyChanged)
    {
        var views = new List<UIElement>();

        try 
        {
            var properties = ReflectionUtils.CreateProperties(obj, context);
            foreach (var property in properties)
            {
                var propertyName = property.PropertyInfo.Name;

                if (!property.PropertyInfo.CanWrite)
                {
                    // I'm assuming that read-only properties should not be inspectable.
                    // This prevents the InstructionSummary property from being displayed, but is possibly
                    // too restrictive? There might be cases where we do want to display a read-only property.
                    continue;
                }

                // Check if this property should be editable, disabled or hidden
                PropertyEditMode propertyEditMode;
                if (obj is IEditable editable)
                {
                    propertyEditMode = editable.GetPropertyEditMode(context, propertyName);
                }
                else
                {
                    propertyEditMode = PropertyEditMode.EditEnabled;
                }


                if (propertyEditMode == PropertyEditMode.Hide)
                {
                    continue;
                }

                var labelText = propertyName.ToHumanFromPascal();

                UIElement view;
                if (property.CollectionType != null)
                {
                    var result = CreatePropertyListView(property, labelText);
                    if (result is ErrorResult<UIElement> error)
                    {
                        var message = ($"Failed to create property list view. {error.Message}");
                        return new ErrorResult<List<UIElement>>(message);
                    }

                    view = result.Data!;
                }
                else
                {
                    var result = CreatePropertyView(property, 0, labelText);
                    if (result.Failure)
                    {
                        var error = result as ErrorResult<UIElement>;
                        var message = ($"Failed to create property view. {error!.Message}");
                        return new ErrorResult<List<UIElement>>(message);
                    }

                    view = result.Data!;
                }

                if (propertyEditMode == PropertyEditMode.EditDisabled)
                {
                    // Disabling a non-interactable UI element (i.e. not a Control) is a no-op - it's already "disabled".
                    if (view is Control control)
                    {
                        // Todo: The label for disabled controls should also be greyed out
                        control.IsEnabled = false;
                    }
                }

                views.Add(view);

                // Start listening for property changes now that the view is created
                property.PropertyChanged += onPropertyChanged;
            }
        }
        catch (Exception ex)
        {
            var message = $"Failed to create property views. {ex.Message}";
            return new ErrorResult<List<UIElement>>(message);
        }

        return new SuccessResult<List<UIElement>>(views);
    }

    private static Result<UIElement> CreatePropertyListView(Property property, string labelText)
    {
        var view = new PropertyListView();
        view.SetProperty(property, labelText);

        return new SuccessResult<UIElement>(view);
    }

    public static Result<UIElement> CreatePropertyView(Property property, int itemIndex, string labelText)
    {
        var viewTypeName = property.PropertyAttribute.ViewName;
        Type? viewType = Type.GetType(viewTypeName);
        if (viewType == null)
        {
            return new ErrorResult<UIElement>($"Failed to create Property View with type '{viewTypeName}'");
        }

        // Instantiate the user control for this ViewName
        var userControl = Activator.CreateInstance(viewType) as UIElement; // create a userControl of the class
        Guard.IsNotNull(userControl);

        var propertyView = userControl as IPropertyView;
        Guard.IsNotNull(propertyView);

        propertyView.SetProperty(property, labelText);
        propertyView.ItemIndex = itemIndex;

        var result = propertyView.CreateChildViews();
        if (result is ErrorResult error)
        {
            var message = ($"Failed to create child views. {error.Message}");
            return new ErrorResult<UIElement>(message);
        }

        return new SuccessResult<UIElement>(userControl);
    }
}
