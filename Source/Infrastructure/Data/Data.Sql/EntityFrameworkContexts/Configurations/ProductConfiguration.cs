using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Sql.EntityFrameworkContexts.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<ProductEntity>
{
    public void Configure(EntityTypeBuilder<ProductEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).HasColumnName("id");

        builder.Property(product => product.Sku)
            .HasColumnName("sku")
            .HasMaxLength(ProductEntity.SkuMaxLength)
            .IsRequired();

        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(ProductEntity.NameMaxLength)
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasMaxLength(ProductEntity.DescriptionMaxLength);

        builder.OwnsOne(product => product.Price, price =>
        {
            price.Property(money => money.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 2)
                .IsRequired();

            price.Property(money => money.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(Money.CurrencyCodeLength)
                .IsRequired();
        });

        builder.Property(product => product.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(product => product.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(product => product.CreatedBy).HasColumnName("created_by").HasMaxLength(256);
        builder.Property(product => product.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(product => product.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);

        builder.Property(product => product.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(product => product.DeletedAtUtc).HasColumnName("deleted_at_utc");
        builder.Property(product => product.DeletedBy).HasColumnName("deleted_by").HasMaxLength(256);

        builder.HasIndex(product => product.Sku).IsUnique().HasDatabaseName("ix_products_sku");

        builder.HasIndex(product => product.CreatedAtUtc).HasDatabaseName("ix_products_created_at_utc");

        builder.Ignore(product => product.DomainEvents);
    }
}
