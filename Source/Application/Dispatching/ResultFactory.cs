using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Domain.Extensions;
using Domain.Results;

namespace Application.Dispatching;

internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> _failureMethods = new();

    private static readonly MethodInfo _genericFailureMethod =
        typeof(Result)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true }
                && method.GetParameters().Length == 1);

    public static TResponse Failure<TResponse>(Error error)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(error);
        }

        var method = _failureMethods.GetOrAdd(
            typeof(TResponse),
            static responseType =>
            {
                var valueType = responseType.GetGenericArguments()[0];

                return _genericFailureMethod.MakeGenericMethod(valueType);
            });

        return (TResponse)method.Invoke(null, [error])!;
    }

    public static object? GetValue<TResponse>(TResponse response)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return null;
        }

        var property = _valueProperties.GetOrAdd(
            typeof(TResponse),
            static responseType => responseType.GetProperty(nameof(Result<object>.Value))!);

        return property.GetValue(response);
    }

    public static TResponse Success<TResponse>(string? payloadJson)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Success();
        }

        var valueType = typeof(TResponse).GetGenericArguments()[0];

        var value = string.IsNullOrWhiteSpace(payloadJson)
            ? null
            : JsonSerializer.Deserialize(payloadJson, valueType, JsonDefaults.Standard);

        var method = _successMethods.GetOrAdd(
            typeof(TResponse),
            static responseType =>
                _genericSuccessMethod.MakeGenericMethod(responseType.GetGenericArguments()[0]));

        return (TResponse)method.Invoke(null, [value])!;
    }

    private static readonly ConcurrentDictionary<Type, MethodInfo> _successMethods = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo> _valueProperties = new();

    private static readonly MethodInfo _genericSuccessMethod =
        typeof(Result)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
                method is { Name: nameof(Result.Success), IsGenericMethodDefinition: true }
                && method.GetParameters().Length == 1);
}
