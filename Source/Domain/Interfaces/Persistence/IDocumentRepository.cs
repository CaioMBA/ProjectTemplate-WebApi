using System.Linq.Expressions;

namespace Domain.Interfaces.Persistence;

public interface IDocumentRepository<TDocument>
    where TDocument : class
{
    Task<TDocument?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TDocument>> ListAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task InsertAsync(TDocument document, CancellationToken cancellationToken = default);

    Task InsertManyAsync(IEnumerable<TDocument> documents, CancellationToken cancellationToken = default);

    Task ReplaceAsync(string id, TDocument document, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<long> CountAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default);
}
