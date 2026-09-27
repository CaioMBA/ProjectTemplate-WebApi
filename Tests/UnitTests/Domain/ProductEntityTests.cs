using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace UnitTests.Domain;

public sealed class ProductEntityTests
{
    private static Money Usd(decimal amount)
    {
        var money = Money.Create(amount, "USD");
        money.IsSuccess.ShouldBeTrue();

        return money.Value;
    }

    [Fact]
    public void Create_WithValidInput_SucceedsAndRaisesCreatedEvent()
    {
        var result = ProductEntity.Create("SKU-1", "Widget", "A widget", Usd(9.99m));

        result.IsSuccess.ShouldBeTrue();

        var product = result.Value;

        product.Sku.ShouldBe("SKU-1");
        product.IsActive.ShouldBeTrue();
        product.DomainEvents.Count.ShouldBe(1);
        product.DomainEvents[0].ShouldBeOfType<ProductCreatedDomainEvent>();
    }

    [Fact]
    public void Create_AssignsATimeOrderedUuidV7Key()
    {
        var first = ProductEntity.Create("SKU-A", "A", null, Usd(1m)).Value;
        var second = ProductEntity.Create("SKU-B", "B", null, Usd(1m)).Value;

        first.Id.ToString()[14].ShouldBe('7');
        second.Id.CompareTo(first.Id).ShouldNotBe(0);
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        var product = ProductEntity.Create("  SKU-2  ", "  Widget  ", "  desc  ", Usd(1m)).Value;

        product.Sku.ShouldBe("SKU-2");
        product.Name.ShouldBe("Widget");
        product.Description.ShouldBe("desc");
    }

    [Theory]
    [InlineData("", "Name", "Product.SkuRequired")]
    [InlineData("   ", "Name", "Product.SkuRequired")]
    [InlineData("SKU", "", "Product.NameRequired")]
    public void Create_WithInvalidInput_ReturnsValidationFailure(string sku, string name, string expectedCode)
    {
        var result = ProductEntity.Create(sku, name, null, Usd(1m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(expectedCode);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_WithOversizedSku_ReturnsValidationFailure()
    {
        var result = ProductEntity.Create(new string('x', ProductEntity.SkuMaxLength + 1), "Name", null, Usd(1m));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.SkuTooLong");
    }

    [Fact]
    public void ChangePrice_ToADifferentAmount_RaisesPriceChangedEvent()
    {
        var product = ProductEntity.Create("SKU-3", "Widget", null, Usd(10m)).Value;
        product.ClearDomainEvents();

        var result = product.ChangePrice(Usd(12m));

        result.IsSuccess.ShouldBeTrue();
        product.Price.Amount.ShouldBe(12m);

        var raised = product.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProductPriceChangedDomainEvent>();

        raised.PreviousAmount.ShouldBe(10m);
        raised.NewAmount.ShouldBe(12m);
    }

    [Fact]
    public void ChangePrice_ToTheSameAmount_RaisesNoEvent()
    {
        var product = ProductEntity.Create("SKU-4", "Widget", null, Usd(10m)).Value;
        product.ClearDomainEvents();

        var result = product.ChangePrice(Usd(10m));

        result.IsSuccess.ShouldBeTrue();

        product.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ChangePrice_ToADifferentCurrency_IsRejected()
    {
        var product = ProductEntity.Create("SKU-5", "Widget", null, Usd(10m)).Value;

        var eur = Money.Create(10m, "EUR").Value;

        var result = product.ChangePrice(eur);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Product.CurrencyMismatch");
        product.Price.Currency.ShouldBe("USD");
    }

    [Fact]
    public void ClearDomainEvents_EmptiesThePendingList()
    {
        var product = ProductEntity.Create("SKU-6", "Widget", null, Usd(1m)).Value;

        product.DomainEvents.ShouldNotBeEmpty();

        product.ClearDomainEvents();

        product.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Entities_CompareByIdentityNotByValue()
    {
        var first = ProductEntity.Create("SKU-7", "Widget", null, Usd(1m)).Value;
        var second = ProductEntity.Create("SKU-7", "Widget", null, Usd(1m)).Value;

        first.ShouldNotBe(second);
        first.ShouldBe(first);
    }

    [Fact]
    public void Money_ComparesByValueNotByReference()
    {
        var first = Usd(10.00m);
        var second = Usd(10.00m);

        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
    }

    [Fact]
    public void Money_RoundsAwayFromZeroToTwoPlaces()
    {
        Usd(2.005m).Amount.ShouldBe(2.01m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Money_RejectsInvalidCurrencyCodes(string currency)
    {
        Money.Create(1m, currency).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Money_RejectsNegativeAmounts()
    {
        Money.Create(-1m, "USD").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Money_NormalisesCurrencyToUpperCase()
    {
        Money.Create(1m, "usd").Value.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Money_Add_RejectsMismatchedCurrencies()
    {
        var usd = Usd(1m);
        var eur = Money.Create(1m, "EUR").Value;

        Should.Throw<InvalidOperationException>(() => usd.Add(eur));
    }
}
