using System.Linq.Expressions;
using Domain.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Data.NoSql.Repositories;

public sealed class MongoDocumentRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class
{
    private readonly IMongoCollection<TDocument> _collection;

    public MongoDocumentRepository([ServiceKey] string databaseId, IServiceProvider services)
    {
        var database = services.GetRequiredKeyedService<IMongoDatabase>(databaseId);

        _collection = database.GetCollection<TDocument>(ResolveCollectionName());
    }

    public async Task<TDocument?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<TDocument>.Filter.Eq("_id", id);

        return await _collection
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TDocument>> ListAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return await _collection
            .Find(predicate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task InsertAsync(TDocument document, CancellationToken cancellationToken = default) =>
        _collection.InsertOneAsync(document, options: null, cancellationToken);

    public Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default) =>
        _collection.InsertManyAsync(documents, options: null, cancellationToken);

    public async Task ReplaceAsync(
        string id,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<TDocument>.Filter.Eq("_id", id);

        await _collection
            .ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = false }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<TDocument>.Filter.Eq("_id", id);

        var result = await _collection
            .DeleteOneAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    public Task<long> CountAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return _collection.CountDocumentsAsync(predicate, options: null, cancellationToken);
    }

    private static string ResolveCollectionName()
    {
        var name = typeof(TDocument).Name;

        if (name.EndsWith("Document", StringComparison.Ordinal))
        {
            name = name[..^"Document".Length];
        }
        else if (name.EndsWith("Entity", StringComparison.Ordinal))
        {
            name = name[..^"Entity".Length];
        }

        return $"{name.ToLowerInvariant()}s";
    }
}
