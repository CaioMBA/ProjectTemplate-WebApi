using System.Linq.Expressions;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Data.NoSql.Providers;
using Domain.Extensions;
using Domain.Interfaces.Persistence;
using Domain.Models.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Data.NoSql.Repositories;

public sealed class DynamoDocumentRepository<TDocument> : IDocumentRepository<TDocument>
    where TDocument : class
{
    public const string HashKeyName = "id";

    private readonly Table _table;

    private readonly int _maxScanPageSize;

    public DynamoDocumentRepository([ServiceKey] string databaseId, IServiceProvider services)
    {
        var client = services.GetRequiredKeyedService<IAmazonDynamoDB>(databaseId);
        var options = services.GetRequiredKeyedService<DynamoTableOptions>(databaseId);

        var name = string.IsNullOrWhiteSpace(options.TablePrefix)
            ? DocumentNaming.CollectionFor<TDocument>()
            : $"{options.TablePrefix}-{DocumentNaming.CollectionFor<TDocument>()}";

        _table = new TableBuilder(client, name)
            .AddHashKey(HashKeyName, DynamoDBEntryType.String)
            .Build();

        _maxScanPageSize = options.MaxScanPageSize;
    }

    public async Task<TDocument?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var document = await _table.GetItemAsync(id, cancellationToken).ConfigureAwait(false);

        return document is null ? null : Deserialize(document);
    }

    public async Task<IReadOnlyList<TDocument>> ListAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matches = predicate.Compile();

        var results = new List<TDocument>();

        await foreach (var document in ScanAsync(cancellationToken).ConfigureAwait(false))
        {
            if (matches(document))
            {
                results.Add(document);
            }
        }

        return results;
    }

    public async Task InsertAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _table
            .PutItemAsync(Serialize(document), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var batch = _table.CreateBatchWrite();

        foreach (var document in documents)
        {
            batch.AddDocumentToPut(Serialize(document));
        }

        await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReplaceAsync(
        string id,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(document);

        var serialized = Serialize(document);

        serialized[HashKeyName] = id;

        await _table.PutItemAsync(serialized, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var existing = await _table.GetItemAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        await _table.DeleteItemAsync(id, cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<long> CountAsync(
        Expression<Func<TDocument, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matches = predicate.Compile();

        long count = 0;

        await foreach (var document in ScanAsync(cancellationToken).ConfigureAwait(false))
        {
            if (matches(document))
            {
                count++;
            }
        }

        return count;
    }

    private async IAsyncEnumerable<TDocument> ScanAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var search = _table.Scan(new ScanOperationConfig
        {
            Limit = _maxScanPageSize,
        });

        do
        {
            var page = await search.GetNextSetAsync(cancellationToken).ConfigureAwait(false);

            foreach (var document in page)
            {
                yield return Deserialize(document);
            }
        }
        while (!search.IsDone);
    }

    private static Document Serialize(TDocument document) =>
        Document.FromJson(document.ToJson());

    private static TDocument Deserialize(Document document) =>
        document.ToJson().ToObject<TDocument>()
        ?? throw new InvalidOperationException(
            $"A DynamoDB item could not be deserialized into {typeof(TDocument).Name}.");
}
