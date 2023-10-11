using System;

namespace Celbridge.Models
{
    public class CallArgumentsPropertyAttribute : PropertyAttribute
    {
        public CallArgumentsPropertyAttribute()
            : base("Celbridge.Views.CallArgumentsPropertyView", typeof(CallArguments))
        {}

        public CallArgumentsPropertyAttribute(string viewName, Type propertyType) : base(viewName, propertyType)
        {}
    }
}

