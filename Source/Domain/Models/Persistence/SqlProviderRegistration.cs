using Domain.Models.Configuration;

namespace Domain.Models.Persistence;

public sealed record SqlProviderRegistration(
    string ConnectionString,
    DatabaseSettings Connection,
    bool RecordStatements);