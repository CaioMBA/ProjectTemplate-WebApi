using Data.NoSql.Providers;
using Shouldly;

namespace UnitTests.NoSql;

public sealed class DocumentNamingTests
{
    private sealed class OrderDocument
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class ProductEntity
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class AuditRecord
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class Category
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class Batch
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class Address
    {
        public string Id { get; set; } = string.Empty;
    }

    [Fact]
    public void StripsDocumentSuffix() =>
        DocumentNaming.CollectionFor<OrderDocument>().ShouldBe("orders");

    [Fact]
    public void StripsEntitySuffix() =>
        DocumentNaming.CollectionFor<ProductEntity>().ShouldBe("products");

    [Fact]
    public void StripsRecordSuffix() =>
        DocumentNaming.CollectionFor<AuditRecord>().ShouldBe("audits");

    [Fact]
    public void PluralizesConsonantYAsIes() =>
        DocumentNaming.CollectionFor<Category>().ShouldBe("categories");

    [Fact]
    public void PluralizesChAsEs() =>
        DocumentNaming.CollectionFor<Batch>().ShouldBe("batches");

    [Fact]
    public void PluralizesSAsEs() =>
        DocumentNaming.CollectionFor<Address>().ShouldBe("addresses");
}
