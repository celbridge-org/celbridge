using System;

namespace Celbridge.Models
{
    public class RecordPropertyAttribute : PropertyAttribute
    {
        public RecordPropertyAttribute()
            : base("Celbridge.Views.RecordPropertyView", typeof(IRecord))
        {}

        public RecordPropertyAttribute(string viewName, Type propertyType) : base(viewName, propertyType)
        {}
    }
}

