using System.Reflection;

namespace Celbridge.Console.Services;

/// <summary>
/// Utility class to generate method signatures for the Help() command
/// </summary>
public class MethodPrinter
{
    private static readonly Dictionary<string, string> TypeMappings = new Dictionary<string, string>
    {
        { "String", "string" },
        { "Boolean", "bool" },
        { "Int32", "int" },
        { "Int64", "long" },
        { "Double", "double" },
        { "Single", "float" },
        { "Decimal", "decimal" },
        { "Char", "char" },
        { "Object", "object" },
        { "Void", "void" },
        { "ResourceKey", "string" } // string is interchangeable with ResourceKey and easier to understand
    };

    public List<string> GetMethodSignatures(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
        var methodSignatures = new List<string>();

        foreach (var method in methods)
        {
            string returnType = GetMappedTypeName(method.ReturnType.Name);
            string methodName = method.Name;

            var parameters = method.GetParameters()
                                   .Select(p => $"{GetMappedTypeName(p.ParameterType.Name)} {p.Name}")
                                   .ToArray();

            // Omit "void" for methods with no return type to simplify output
            string methodSignature = returnType == "void"
                ? $"{methodName}({string.Join(", ", parameters)})"
                : $"{returnType} {methodName}({string.Join(", ", parameters)})";

            methodSignatures.Add(methodSignature);
        }

        return methodSignatures;
    }

    private string GetMappedTypeName(string typeName)
    {
        return TypeMappings.ContainsKey(typeName) ? TypeMappings[typeName] : typeName;
    }
}

