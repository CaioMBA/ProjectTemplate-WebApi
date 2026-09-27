using System.Numerics;
using System.Runtime.CompilerServices;
using Domain.Exceptions;

namespace Domain.Guards;

public static class Guard
{
    public static T AgainstNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value is null)
        {
            throw new DomainException($"'{parameterName}' must not be null.");
        }

        return value;
    }

    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"'{parameterName}' must not be null or whitespace.");
        }

        return value;
    }

    public static string AgainstTooLong(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        AgainstNull(value, parameterName);

        if (value.Length > maxLength)
        {
            throw new DomainException(
                $"'{parameterName}' must be {maxLength} characters or fewer, but was {value.Length}.");
        }

        return value;
    }

    public static T AgainstNegative<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : INumber<T>
    {
        if (T.IsNegative(value))
        {
            throw new DomainException($"'{parameterName}' must not be negative, but was {value}.");
        }

        return value;
    }

    public static T AgainstNegativeOrZero<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : INumber<T>
    {
        if (T.IsNegative(value) || T.IsZero(value))
        {
            throw new DomainException($"'{parameterName}' must be greater than zero, but was {value}.");
        }

        return value;
    }

    public static T AgainstOutOfRange<T>(
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new DomainException($"'{parameterName}' must be between {min} and {max}, but was {value}.");
        }

        return value;
    }

    public static T AgainstDefault<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : struct
    {
        if (EqualityComparer<T>.Default.Equals(value, default))
        {
            throw new DomainException($"'{parameterName}' must not be the default value.");
        }

        return value;
    }

    public static void Against(bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(message);
        }
    }
}
