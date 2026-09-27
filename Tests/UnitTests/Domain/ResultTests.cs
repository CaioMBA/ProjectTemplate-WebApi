using Domain.Enums;
using Domain.Results;

namespace UnitTests.Domain;

public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccessful_AndCarriesNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_IsNotSuccessful_AndCarriesTheError()
    {
        var error = Error.NotFound("Product.NotFound", "No such product.");

        var result = Result.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void SuccessOfT_ExposesTheValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void FailureOfT_ThrowsWhenTheValueIsRead()
    {
        var result = Result.Failure<int>(Error.Validation("Test.Invalid", "Bad input."));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Create_WrapsNonNullAsSuccess()
    {
        var result = Result.Create("value");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("value");
    }

    [Fact]
    public void Create_WrapsNullAsNullValueFailure()
    {
        var result = Result.Create<string>(null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.NullValue);
    }

    [Fact]
    public void Error_IsValueEqual_SoResultsCompareByContent()
    {
        var first = Error.Conflict("Product.Duplicate", "Already exists.");
        var second = Error.Conflict("Product.Duplicate", "Already exists.");

        first.ShouldBe(second);
    }
}
