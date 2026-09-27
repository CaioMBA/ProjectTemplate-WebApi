using System.Diagnostics.CodeAnalysis;

namespace Domain.Results;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
            case false when error == Error.None:
                throw new ArgumentException("A failed result must carry an error.", nameof(error));
            default:
                break;
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    public static Result<TValue> Create<TValue>(TValue? value)
        where TValue : class =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);
}

public class Result<TValue> : Result
{
    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => RawValue = value;

    private TValue? RawValue { get; }

    [NotNull]
    public TValue Value => IsSuccess
        ? RawValue!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");
}
