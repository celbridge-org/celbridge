using Celbridge.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celbridge.Utils
{
    public static class ReflectionUtils
    {
        public static TReturn CallStaticMethod<TReturn>(this Type t, string method, object obj = null, params object[] parameters)
        {
            return (TReturn)t.GetMethod(method)?.Invoke(obj, parameters);
        }

        // Returns a list of attributes declared on the property.
        // For IRecord properties, also returns any attributes declared on the record type.
        public static List<Attribute> GetCustomAttributes(PropertyInfo propertyInfo, Type collectionType)
        {
            var attributes = new List<Attribute>();

            var encounteredTypes = new HashSet<Type>();

            // Attributes default to single use per entity, so we're assuming that behaviour.
            // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes#2222-attribute-usage

            // Add attributes declared directly on the property
            foreach (var attribute in propertyInfo.GetCustomAttributes())
            {
                attributes.Add(attribute);
                encounteredTypes.Add(attribute.GetType());
            }

            // For IRecord properties, also add any attributes declared on the record type.
            Type propertyType = collectionType ?? propertyInfo.PropertyType;
            if (typeof(IRecord).IsAssignableFrom(propertyType))
            {
                var propertyAttributes = propertyType.GetCustomAttributes();
                foreach (var attr in propertyAttributes)
                {
                    if (encounteredTypes.Contains(attr.GetType()))
                    {
                        // Ignore this attribute on the record type because an overriding attribute is declared on the property.
                        continue;
                    }

                    attributes.Add(attr);
                    encounteredTypes.Add(attr.GetType());
                }
            }

            return attributes;
        }

        public static List<Property> CreateProperties(object obj, PropertyContext context)
        {
            List<Property> properties = new();

            var entityType = obj.GetType();
            var propertyInfos = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(propInfo => propInfo.DeclaringType == entityType ? 1 : 0)
                .ToArray();

            foreach (var propertyInfo in propertyInfos)
            {
                // Ignore any property with the HideProperty attribute
                if (propertyInfo.GetCustomAttribute<HidePropertyAttribute>() != null)
                {
                    continue;
                }

                // Get the c# type of this property
                var propertyType = propertyInfo.PropertyType;

                // Get (or default to) a valid Property attribute
                var propertyAttribute = Microsoft.Scripting.Utils.ReflectionUtils.GetCustomAttribute<PropertyAttribute>(propertyInfo, true);
                if (propertyAttribute == null)
                {
                    var recordType = GetRecordType(propertyType);
                    if (recordType != null)
                    {
                        // This call will return a derived RecordPropertyAttribute (e.g. InstructionLineProperty) if
                        // one is declared on the record type.
                        propertyAttribute = recordType.GetCustomAttribute<RecordPropertyAttribute>();
                    }                        
                }

                if (propertyAttribute == null)
                {
                    // Try to assign a default property attribute based on the property's type
                    var result = GetDefaultPropertyAttribute(propertyType);
                    if (result.Success)
                    {
                        propertyAttribute = result.Data;
                    }
                }

                if (propertyAttribute == null)
                {
                    // Ignore properties which don't have a PropertyAttribute and can't be assigned a default one
                    continue;
                }

                // Override the context if a PropertyContext attribute exists on this property
                // This then becomes the default context assigned to all child properties
                var propertyContextAttribute = propertyInfo.GetCustomAttribute<PropertyContextAttribute>();
                if (propertyContextAttribute != null)
                {
                    context = propertyContextAttribute.Context;
                }

                var matchesType = AreTypesCompatible(propertyType, propertyAttribute.PropertyType);
                var isListOfType = !matchesType && IsListOfElementType(propertyType, propertyAttribute.PropertyType);

                if (matchesType || isListOfType)
                {
                    var collectionType = isListOfType ? propertyAttribute.PropertyType : null;
                    var property = new Property(obj, propertyAttribute, propertyInfo, collectionType, context);
                    properties.Add(property);
                }
                else
                {
                    Log.Error($"Attribute '{propertyAttribute}' is not valid on property '{propertyInfo.Name}' of entity type '{propertyType}'");
                }
            }

            return properties;
        }

        private static bool IsListOfElementType(Type listType, Type elementType)
        {
            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var containedType = listType.GetGenericArguments()[0];
                return AreTypesCompatible(containedType, elementType);
            }

            return false;
        }

        private static bool AreTypesCompatible(Type typeA, Type typeB)
        {
            return typeA == typeB ||
                typeA.IsSubclassOf(typeB) ||
                typeA.IsAssignableTo(typeB);
        }

        private static Type GetRecordType(Type propertyType)
        {
            // Look for a RecordPropertyAttribute (or a derived attribute) declared on the propertyType
            bool isRecord = AreTypesCompatible(propertyType, typeof(IRecord));
            bool isRecordList = !isRecord && IsListOfElementType(propertyType, typeof(IRecord));

            if (isRecord)
            {
                return propertyType;
            }
            else if (isRecordList)
            {
                var containedType = propertyType.GetGenericArguments()[0];
                return containedType;
            }

            return null;
        }

        private static Result<PropertyAttribute> GetDefaultPropertyAttribute(Type propertyType)
        {
            static bool MatchType(Type typeA, Type typeB)
            {
                return (AreTypesCompatible(typeA, typeB) || IsListOfElementType(typeA, typeB));
            }

            PropertyAttribute propertyAttribute = null; ;
            if (MatchType(propertyType, typeof(string)))
            {
                propertyAttribute = new TextPropertyAttribute();
            }
            else if (MatchType(propertyType, typeof(double)))
            {
                propertyAttribute = new NumberPropertyAttribute();
            }
            else if (MatchType(propertyType, typeof(bool)))
            {
                propertyAttribute = new BooleanPropertyAttribute();
            }
            else if (MatchType(propertyType, typeof(ExpressionBase)))
            {
                propertyAttribute = new ExpressionPropertyAttribute();
            }
            else if (MatchType(propertyType, typeof(IRecord)))
            {
                propertyAttribute = new RecordPropertyAttribute();
            }

            if (propertyAttribute == null)
            {
                return new ErrorResult<PropertyAttribute>($"Failed to get default property attribute for type '{propertyType}'");
            }

            return new SuccessResult<PropertyAttribute>(propertyAttribute);
        }

        public static Result<Type> FindTypeInAssembly(Assembly assembly, string className, string @namespace)
        {
            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                if (type.Name == className && type.Namespace == @namespace)
                {
                    return new SuccessResult<Type>(type);
                }
            }

            return new ErrorResult<Type>($"Failed to find type '{className}' in namespace '{@namespace}' in assembly '{assembly.GetName()}'");
        }
    }
}
