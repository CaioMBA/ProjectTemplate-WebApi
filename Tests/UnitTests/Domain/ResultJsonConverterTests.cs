using System.Text.Json;
using Domain.DTOs;
using Domain.Enums;
using Domain.Extensions;
using Domain.Results;
using Shouldly;

namespace UnitTests.Domain;

public sealed class ResultJsonConverterTests
{
    private static readonly JsonSerializerOptions _options = JsonDefaults.Standard;

    [Fact]
    public void ASuccessfulGenericResultSurvivesARoundTrip()
    {
        var dto = new ProductDto
        {
            Id = Guid.CreateVersion7(),
            Sku = "SKU-1",
            Name = "Widget",
            Description = "A widget.",
            PriceAmount = 9.99m,
            PriceCurrency = "USD",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(Result.Success(dto), _options);

        var restored = JsonSerializer.Deserialize<Result<ProductDto>>(json, _options);

        restored.ShouldNotBeNull();
        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Sku.ShouldBe("SKU-1");
        restored.Value.PriceAmount.ShouldBe(9.99m);
        restored.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void AFailedGenericResultSurvivesARoundTrip()
    {
        var failure = Result.Failure<ProductDto>(
            Error.NotFound("Product.NotFound", "No product exists."));

        var json = JsonSerializer.Serialize(failure, _options);

        var restored = JsonSerializer.Deserialize<Result<ProductDto>>(json, _options);

        restored.ShouldNotBeNull();
        restored.IsFailure.ShouldBeTrue();
        restored.Error.Code.ShouldBe("Product.NotFound");
        restored.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void ANonGenericResultSurvivesARoundTripInBothStates()
    {
        var success = JsonSerializer.Deserialize<Result>(
            JsonSerializer.Serialize(Result.Success(), _options),
            _options);

        var failure = JsonSerializer.Deserialize<Result>(
            JsonSerializer.Serialize(
                Result.Failure(Error.Conflict("Product.Duplicate", "Already exists.")),
                _options),
            _options);

        success.ShouldNotBeNull();
        success.IsSuccess.ShouldBeTrue();

        failure.ShouldNotBeNull();
        failure.IsFailure.ShouldBeTrue();
        failure.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void AFailedResultDoesNotEmitAValue()
    {
        var json = JsonSerializer.Serialize(
            Result.Failure<ProductDto>(Error.Validation("X", "Y")),
            _options);

        json.ShouldNotContain("\"value\"");
    }

    [Fact]
    public void ASuccessfulResultOfAScalarSurvivesARoundTrip()
    {
        var json = JsonSerializer.Serialize(Result.Success(42), _options);

        var restored = JsonSerializer.Deserialize<Result<int>>(json, _options);

        restored.ShouldNotBeNull();
        restored.IsSuccess.ShouldBeTrue();
        restored.Value.ShouldBe(42);
    }

    [Fact]
    public void TheCacheSerialisationPathRoundTripsAResult()
    {
        var original = Result.Success(new ProductDto
        {
            Id = Guid.CreateVersion7(),
            Sku = "CACHE-1",
            Name = "Cached",
            PriceAmount = 1m,
            PriceCurrency = "EUR",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        });

        var restored = original.ToJsonBytes().FromJsonBytes<Result<ProductDto>>();

        restored.ShouldNotBeNull();
        restored.IsSuccess.ShouldBeTrue();
        restored.Value.Sku.ShouldBe("CACHE-1");
    }
}
