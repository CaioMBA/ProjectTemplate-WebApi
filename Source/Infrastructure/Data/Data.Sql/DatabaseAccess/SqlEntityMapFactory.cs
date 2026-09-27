using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using Domain.Attributes;
using Domain.Enums;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Data.Sql.DatabaseAccess;

public sealed class SqlEntityMapFactory
{
    private static readonly ConcurrentDictionary<Type, SqlEntityMap> _cache = new();

    private readonly IModel _model;

    public SqlEntityMapFactory(IModel model) => this._model = model;

    public SqlEntityMap For<T>() => For(typeof(T));

    public SqlEntityMap For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return _cache.GetOrAdd(entityType, Build);
    }

    private SqlEntityMap Build(Type entityType)
    {
        var mapped = _model.FindEntityType(entityType);

        return mapped is null
            ? BuildFromConvention(entityType)
            : BuildFromEntityFrameworkModel(entityType, mapped);
    }

    private static SqlEntityMap BuildFromEntityFrameworkModel(Type entityType, IEntityType mapped)
    {
        var tableName = mapped.GetTableName()
            ?? throw new InvalidOperationException(
                $"{entityType.Name} is in the Entity Framework model but maps to no table.");

        var columns = new List<SqlColumnMap>();

        AppendProperties(mapped, columns, parentGetter: null, pathPrefix: string.Empty);

        AppendOwnedProperties(mapped, columns, tableName);

        return new SqlEntityMap(
            entityType,
            tableName,
            mapped.GetSchema(),
            columns,
            SqlMapSource.EntityFrameworkModel);
    }

    private static void AppendProperties(
        IEntityType mapped,
        List<SqlColumnMap> columns,
        Func<object, object?>? parentGetter,
        string pathPrefix)
    {
        foreach (var property in mapped.GetProperties())
        {
            if (property.IsShadowProperty())
            {
                continue;
            }

            var columnName = property.GetColumnName();

            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            var accessor = property.PropertyInfo;

            if (accessor is null)
            {
                continue;
            }

            columns.Add(new SqlColumnMap(
                columnName,
                pathPrefix + accessor.Name,
                Compose(parentGetter, accessor),
                property.IsPrimaryKey(),
                property.ValueGenerated is ValueGenerated.OnAdd or ValueGenerated.OnAddOrUpdate
                    && property.GetDefaultValueSql() is not null));
        }
    }

    private static void AppendOwnedProperties(
        IEntityType mapped,
        List<SqlColumnMap> columns,
        string tableName)
    {
        foreach (var navigation in mapped.GetNavigations())
        {
            var target = navigation.TargetEntityType;

            if (!target.IsOwned() || navigation.IsCollection)
            {
                continue;
            }

            if (!string.Equals(target.GetTableName(), tableName, StringComparison.Ordinal))
            {
                continue;
            }

            var navigationAccessor = navigation.PropertyInfo;

            if (navigationAccessor is null)
            {
                continue;
            }

            AppendProperties(
                target,
                columns,
                Compose(parentGetter: null, navigationAccessor),
                $"{navigationAccessor.Name}.");
        }
    }

    private static SqlEntityMap BuildFromConvention(Type entityType)
    {
        var table = entityType.GetCustomAttribute<SqlTableAttribute>();

        var columns = new List<SqlColumnMap>();

        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<SqlIgnoreAttribute>() is not null
                || !property.CanRead)
            {
                continue;
            }

            var key = property.GetCustomAttribute<SqlKeyAttribute>();

            var isKey = key is not null
                || string.Equals(property.Name, "Id", StringComparison.Ordinal);

            columns.Add(new SqlColumnMap(
                property.GetCustomAttribute<SqlColumnAttribute>()?.Name ?? ToSnakeCase(property.Name),
                property.Name,
                Compose(parentGetter: null, property),
                isKey,
                key?.DatabaseGenerated ?? false));
        }

        return new SqlEntityMap(
            entityType,
            table?.Name ?? ToSnakeCase(Pluralize(entityType.Name)),
            table?.Schema,
            columns,
            SqlMapSource.Convention);
    }

    private static Func<object, object?> Compose(
        Func<object, object?>? parentGetter,
        PropertyInfo accessor) =>
        parentGetter is null
            ? instance => accessor.GetValue(instance)
            : instance =>
            {
                var parent = parentGetter(instance);

                return parent is null ? null : accessor.GetValue(parent);
            };

    private static string Pluralize(string name)
    {
        var trimmed = name.EndsWith("Entity", StringComparison.Ordinal)
            ? name[..^"Entity".Length]
            : name;

        if (trimmed.EndsWith('y') && trimmed.Length > 1 && !IsVowel(trimmed[^2]))
        {
            return string.Concat(trimmed.AsSpan(0, trimmed.Length - 1), "ies");
        }

        return trimmed.EndsWith('s') || trimmed.EndsWith("ch", StringComparison.Ordinal)
            ? trimmed + "es"
            : trimmed + "s";
    }

    private static bool IsVowel(char value) => "aeiouAEIOU".Contains(value, StringComparison.Ordinal);

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];

            if (char.IsUpper(current))
            {
                var isBoundary = index > 0
                    && (!char.IsUpper(name[index - 1])
                        || (index + 1 < name.Length && char.IsLower(name[index + 1])));

                if (isBoundary)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLower(current, CultureInfo.InvariantCulture));

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
