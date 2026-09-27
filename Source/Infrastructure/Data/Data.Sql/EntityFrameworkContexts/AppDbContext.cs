using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Data.Sql.EntityFrameworkContexts.Comparers;
using Data.Sql.EntityFrameworkContexts.Converters;
using Data.Sql.EntityFrameworkContexts.ValueGenerators;
using Domain.Abstractions;
using Domain.Entities;
using Domain.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NCrontab;

namespace Data.Sql.EntityFrameworkContexts;

public abstract class AppDbContext(DbContextOptions options) : DbContext(options)
{
    protected abstract ISqlDialect Dialect { get; }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        ApplySoftDeleteQueryFilters(modelBuilder);
        ApplyGuidV7KeyGeneration(modelBuilder);
        ApplyJsonValueComparers(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        var jsonColumnType = Dialect.JsonColumnType;

        configurationBuilder.Properties<DateOnly>().HaveConversion<DateOnlyConverter>();
        configurationBuilder.Properties<DateOnly?>().HaveConversion<NullableDateOnlyConverter>();
        configurationBuilder.Properties<TimeOnly>().HaveConversion<TimeOnlyConverter>();
        configurationBuilder.Properties<TimeOnly?>().HaveConversion<NullableTimeOnlyConverter>();

        configurationBuilder.Properties<CrontabSchedule>().HaveConversion<CronExpressionConverter>();

        configurationBuilder.Properties<JsonObject>()
            .HaveConversion<JsonObjectConverter>()
            .HaveColumnType(jsonColumnType);
        configurationBuilder.Properties<JsonArray>()
            .HaveConversion<JsonArrayConverter>()
            .HaveColumnType(jsonColumnType);
        configurationBuilder.Properties<JsonNode>()
            .HaveConversion<JsonNodeConverter>()
            .HaveColumnType(jsonColumnType);

        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        var softDeletableTypes = modelBuilder.Model
            .GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .Where(clrType => typeof(ISoftDeletable).IsAssignableFrom(clrType));

        foreach (var clrType in softDeletableTypes)
        {
            var parameter = Expression.Parameter(clrType, "entity");

            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)),
                Expression.Constant(false));

            modelBuilder
                .Entity(clrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    private static void ApplyGuidV7KeyGeneration(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsKey() && property.ClrType == typeof(Guid))
                {
                    property.SetValueGeneratorFactory((_, _) => new GuidV7ValueGenerator());
                }
            }
        }
    }

    private static void ApplyJsonValueComparers(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                ValueComparer? comparer = property.ClrType switch
                {
                    var type when type == typeof(JsonObject) => JsonValueComparers.JsonObjectComparer,
                    var type when type == typeof(JsonArray) => JsonValueComparers.JsonArrayComparer,
                    var type when type == typeof(JsonNode) => JsonValueComparers.JsonNodeComparer,
                    _ => null,
                };

                if (comparer is not null)
                {
                    property.SetValueComparer(comparer);
                }
            }
        }
    }
}
