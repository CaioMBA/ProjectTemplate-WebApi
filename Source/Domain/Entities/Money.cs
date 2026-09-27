using Domain.Abstractions;
using Domain.Extensions;
using Domain.Results;

namespace Domain.Entities;

public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public const int CurrencyCodeLength = 3;

    public decimal Amount { get; }

    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return Result.Failure<Money>(Error.Validation("Money.CurrencyRequired", "Currency is required."));
        }

        var normalised = currency.Trim().ToUpperInvariant();

        if (normalised.Length != CurrencyCodeLength)
        {
            return Result.Failure<Money>(Error.Validation(
                "Money.CurrencyInvalid",
                $"Currency must be a {CurrencyCodeLength}-letter ISO 4217 code."));
        }

        if (amount < 0)
        {
            return Result.Failure<Money>(Error.Validation("Money.AmountNegative", "Amount cannot be negative."));
        }

        return Result.Success(new Money(amount.RoundTo(2), normalised));
    }

    public static Money Zero(string currency) => new(0m, currency.Trim().ToUpperInvariant());

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return other.Currency != Currency
            ? throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}.")
            : new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new((Amount * factor).RoundTo(2), Currency);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount.ToMoneyString()} {Currency}";
}
