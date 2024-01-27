namespace Celbridge.Legacy.Models;

public class RecordPropertyAttribute : PropertyAttribute
{
    public RecordPropertyAttribute()
        : base("Celbridge.Legacy.Views.RecordPropertyView", typeof(IRecord))
    {}

    public RecordPropertyAttribute(string viewName, Type propertyType) : base(viewName, propertyType)
    {}
}

