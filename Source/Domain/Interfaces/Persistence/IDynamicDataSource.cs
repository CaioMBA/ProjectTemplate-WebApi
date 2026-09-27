using Domain.Enums;
using Domain.Models.DynamicData;

namespace Domain.Interfaces.Persistence;

public interface IDynamicDataSource
{
    Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default);

    Task<long> CountAsync(
        EntitySetModel set,
        FilterNode? filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default);
}
