namespace Domain.Interfaces.Persistence;

public interface ISqlSyntax
{
    string QuoteIdentifier(string name);

    string Parameter(string name);

    string BooleanLiteral(bool value);

    string CaseInsensitiveLike(string column, string parameter);

    string ApplyPagination(string sql, int offset, int limit);

    string EscapeLikePattern(string value);
}
