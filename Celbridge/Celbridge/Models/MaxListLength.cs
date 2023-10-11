using System;

namespace Celbridge.Models
{
    public class MaxListLengthAttribute : Attribute
    {
        public int MaxLength { get; private set; }

        public MaxListLengthAttribute(int maxLength)
        {
            MaxLength = maxLength;
        }
    }
}
