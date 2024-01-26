namespace CelLegacy.Models;

public class RecordPropertyAttribute : PropertyAttribute
{
    public RecordPropertyAttribute()
        : base("CelLegacy.Views.RecordPropertyView", typeof(IRecord))
    {}

    public RecordPropertyAttribute(string viewName, Type propertyType) : base(viewName, propertyType)
    {}
}

