using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Domain.Attributes;
using Domain.Enums;
using Domain.Models.Configuration;

namespace CrossCutting.Configuration;

internal static class SecretPropertyPlan
{
    private const string ConfigurationModelNamespace = "Domain.Models";

    private static readonly ConcurrentDictionary<Type, SecretProperty[]> _plans = new();

    public static SecretProperty[] For(Type type) => _plans.GetOrAdd(type, Build);

    private static SecretProperty[] Build(Type type)
    {
        var plan = new List<SecretProperty>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var secret = property.GetCustomAttribute<SecretAttribute>();

            if (secret is not null)
            {
                if (property.PropertyType == typeof(string) && property.CanWrite)
                {
                    plan.Add(new SecretProperty(property, SecretPropertyKind.Secret, secret.Required));
                }

                continue;
            }

            var kind = Classify(property.PropertyType);

            if (kind != SecretPropertyKind.Ignored)
            {
                plan.Add(new SecretProperty(property, kind, Required: false));
            }
        }

        return [.. plan];
    }

    private static SecretPropertyKind Classify(Type propertyType)
    {
        if (propertyType == typeof(string) || propertyType.IsPrimitive || propertyType.IsEnum)
        {
            return SecretPropertyKind.Ignored;
        }

        if (IsConfigurationModel(propertyType))
        {
            return SecretPropertyKind.Nested;
        }

        if (!typeof(IEnumerable).IsAssignableFrom(propertyType))
        {
            return SecretPropertyKind.Ignored;
        }

        var elementType = ResolveElementType(propertyType);

        return elementType is not null && IsConfigurationModel(elementType)
            ? SecretPropertyKind.NestedCollection
            : SecretPropertyKind.Ignored;
    }

    private static Type? ResolveElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType();
        }

        foreach (var contract in collectionType.GetInterfaces().Append(collectionType))
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool IsConfigurationModel(Type type) =>
        type.IsClass
        && !type.IsAbstract
        && type.Namespace is not null
        && type.Namespace.StartsWith(ConfigurationModelNamespace, StringComparison.Ordinal);
}
