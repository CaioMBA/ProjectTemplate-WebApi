using Data.Sql.EntityFrameworkContexts;
using Domain.Abstractions;
using Domain.Interfaces.Persistence;
using Domain.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Sql.Repositories;

public sealed class Repository<TAggregate, TId>(
    [ServiceKey] string databaseId,
    IServiceProvider services) : IRepository<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    private readonly AppDbContext _context = services.GetRequiredKeyedService<AppDbContext>(databaseId);

    public async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await _context.Set<TAggregate>()
            .FirstOrDefaultAsync(aggregate => aggregate.Id.Equals(id), cancellationToken)
            .ConfigureAwait(false);

    public async Task<TAggregate?> FindAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await _context.Set<TAggregate>()
            .FirstOrDefaultAsync(specification.ToExpression(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TAggregate>> ListAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await _context.Set<TAggregate>()
            .Where(specification.ToExpression())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await _context.Set<TAggregate>()
            .AsNoTracking()
            .CountAsync(specification.ToExpression(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> AnyAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await _context.Set<TAggregate>()
            .AsNoTracking()
            .AnyAsync(specification.ToExpression(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default) =>
        await _context.Set<TAggregate>().AddAsync(aggregate, cancellationToken).ConfigureAwait(false);

    public void Update(TAggregate aggregate) => _context.Set<TAggregate>().Update(aggregate);

    public void Remove(TAggregate aggregate) => _context.Set<TAggregate>().Remove(aggregate);
}
