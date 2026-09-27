using System.Linq.Expressions;
using System.Net;
using Data.NoSql.Providers;
using Domain.Interfaces.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Data.NoSql.Repositories;

public sealed class CosmosDocumentRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class
{
    private readonly Container _container;

    public CosmosDocumentRepository([ServiceKey] string databaseId, IServiceProvider services)
    {
        var database = services.GetRequiredKeyedService<Database>(databaseId);

        _container = database.GetContainer(DocumentNaming.CollectionFor<TDocument>());
    }

    public async Task<TDocument?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            var response = await _container
                .ReadItemAsync<TDocument>(id, new PartitionKey(id), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TDocument>> ListAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var iterator = _container
            .GetItemLinqQueryable<TDocument>()
            .Where(predicate)
            .ToFeedIterator();

        var documents = new List<TDocument>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);

            documents.AddRange(page);
        }

        return documents;
    }

    public async Task InsertAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _container
            .CreateItemAsync(document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            await InsertAsync(document, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReplaceAsync(
        string id,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(document);

        await _container
            .ReplaceItemAsync(document, id, new PartitionKey(id), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        try
        {
            await _container
                .DeleteItemAsync<TDocument>(id, new PartitionKey(id), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<long> CountAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var response = await _container
            .GetItemLinqQueryable<TDocument>()
            .Where(predicate)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        return response.Resource;
    }
}
