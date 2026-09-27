namespace Domain.Models.Persistence;

public sealed record DynamoTableOptions(string? TablePrefix, int MaxScanPageSize);