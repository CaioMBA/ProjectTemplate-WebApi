using System.Linq.Expressions;
using Domain.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Data.NoSql.Repositories;

public sealed class RavenDocumentRepository<TDocument>(
    [ServiceKey] string databaseId,
    IServiceProvider services)
    : IDocumentRepository<TDocument>
    where TDocument : class
{
    private readonly IDocumentStore _store = services.GetRequiredKeyedService<IDocumentStore>(databaseId);

    public async Task<TDocument?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var session = _store.OpenAsyncSession();

        return await session.LoadAsync<TDocument>(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TDocument>> ListAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var session = _store.OpenAsyncSession();

        return await session
            .Query<TDocument>()
            .Where(predicate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InsertAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var session = _store.OpenAsyncSession();

        await session.StoreAsync(document, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        using var session = _store.OpenAsyncSession();

        foreach (var document in documents)
        {
            await session.StoreAsync(document, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(
        string id,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(document);

        using var session = _store.OpenAsyncSession();

        await session.StoreAsync(document, id, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var session = _store.OpenAsyncSession();

        var existing = await session.LoadAsync<TDocument>(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        session.Delete(existing);

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<long> CountAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var session = _store.OpenAsyncSession();

        return await session
            .Query<TDocument>()
            .Where(predicate)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
