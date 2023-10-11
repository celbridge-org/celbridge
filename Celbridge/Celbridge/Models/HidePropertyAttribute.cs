using System;

namespace Celbridge.Models
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class HidePropertyAttribute : Attribute
    {}
}
