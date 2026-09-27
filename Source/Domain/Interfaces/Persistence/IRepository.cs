using Domain.Abstractions;
using Domain.Specifications;

namespace Domain.Interfaces.Persistence;

public interface IRepository<TAggregate, in TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    Task<TAggregate?> FindAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TAggregate>> ListAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default);

    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    void Update(TAggregate aggregate);

    void Remove(TAggregate aggregate);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    bool HasActiveTransaction { get; }

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool> shouldCommit,
        CancellationToken cancellationToken = default);
}
