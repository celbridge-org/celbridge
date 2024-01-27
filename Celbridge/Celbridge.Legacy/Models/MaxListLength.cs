namespace Celbridge.Legacy.Models;

public class MaxListLengthAttribute : Attribute
{
    public int MaxLength { get; private set; }

    public MaxListLengthAttribute(int maxLength)
    {
        MaxLength = maxLength;
    }
}
