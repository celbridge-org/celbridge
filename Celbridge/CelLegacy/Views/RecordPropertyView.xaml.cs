using System.Collections;

namespace CelLegacy.Views;

public partial class RecordPropertyView : UserControl, IPropertyView
{
    public RecordPropertyViewModel ViewModel { get; }

    public RecordPropertyView()
    {
        this.InitializeComponent();

        var services = LegacyServiceProvider.Services!;
        ViewModel = services.GetRequiredService<RecordPropertyViewModel>();
    }

    public void SetProperty(Property property, string labelText)
    {
        ViewModel.SetProperty(property, labelText);
    }

    public Result CreateChildViews()
    {
        try
        {
            var property = ViewModel.Property;

            // Get the record object that this property is referencing and create a view for each of its properties.
            IRecord? record;
            if (property.CollectionType != null)
            {
                var list = property.PropertyInfo.GetValue(property.Object) as IList;
                Guard.IsNotNull(list);
                Guard.IsTrue(ItemIndex < list.Count);
                record = list[ItemIndex] as IRecord;
            }
            else
            {
                record = property.PropertyInfo.GetValue(property.Object) as IRecord;
            }

            if (record == null)
            {
                return new ErrorResult($"Failed to get record object for property {property.PropertyInfo.Name}.");
            }

            var result = PropertyViewUtils.CreatePropertyViews(record, PropertyContext.Record, (s, e) =>
            {
                // This callback is called when any of the record's properties are changed.
                // Now we notify the record property itself that it has changed.
                property.NotifyPropertyChanged();
            });

            if (result is ErrorResult<List<UIElement>> error)
            {
                return new ErrorResult(error.Message);
            }

            var views = result.Data!;
            foreach (var view in views)
            {
                PropertyViews.Items.Add(view);
            }

            return new SuccessResult();
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to create child views. {ex.Message}");
        }
    }

    public ItemCollection GetPropertyViews()
    {
        return PropertyViews.Items;
    }

    public int ItemIndex
    {
        get => ViewModel.ItemIndex;
        set => ViewModel.ItemIndex = value;
    }
}
