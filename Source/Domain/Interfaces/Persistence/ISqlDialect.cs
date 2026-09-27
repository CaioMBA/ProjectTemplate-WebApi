using Domain.Enums;

namespace Domain.Interfaces.Persistence;

public interface ISqlDialect : ISqlSyntax
{
    DatabaseType ProviderType { get; }

    string ParameterPrefix { get; }

    string JsonColumnType { get; }

    int MaxIdentifierLength { get; }

    bool SupportsSavepoints { get; }
}
