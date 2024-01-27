using System.Reflection;

namespace Celbridge.Legacy.Utils;

public interface IProperty
{}

public class Property : ObservableObject, IProperty
{
    public object Object { get; private set; }
    public PropertyAttribute PropertyAttribute { get; private set; }

    private List<Attribute>? _attributes;
    public List<Attribute> Attributes
    {
        get
        {
            if (_attributes is null)
            {
                Guard.IsNotNull(CollectionType);

                // Lazily allocate the attributes list.
                _attributes = ReflectionUtils.GetCustomAttributes(PropertyInfo, CollectionType);

                // Remove the PropertyAttribute from the attributes list.
                // This ensures consistent usage of the PropertyAttribute by client code.
                _attributes.Remove(PropertyAttribute);
            }
            return _attributes;
        }
    }

    public PropertyInfo PropertyInfo { get; private set; }
    
    // The type contained in the collection, or null if the property is not a collection.
    public Type? CollectionType { get; private set; }

    public PropertyContext Context { get; private set; }

    public Property(object obj, PropertyAttribute propertyAttribute, PropertyInfo propertyInfo, Type? collectionType, PropertyContext context)
    {
        Object = obj;
        PropertyInfo = propertyInfo;
        PropertyAttribute = propertyAttribute;
        CollectionType = collectionType;
        Context = context;
    }

    public void NotifyPropertyChanged()
    {
        OnPropertyChanged(PropertyInfo.Name);
    }
}
