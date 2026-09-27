using System.Data;
using System.Globalization;
using Dapper;

namespace Data.Sql.DapperHandlers;

public sealed class TextGuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override Guid Parse(object value) => value switch
    {
        Guid guid => guid,
        string text => Guid.Parse(text, CultureInfo.InvariantCulture),
        byte[] bytes => new Guid(bytes),
        _ => Guid.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidCastException($"Cannot read a Guid from '{value.GetType()}'."),
            CultureInfo.InvariantCulture),
    };

    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.DbType = DbType.String;
        parameter.Value = value.ToString("D", CultureInfo.InvariantCulture);
    }
}

public sealed class TextDateTimeTypeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override DateTime Parse(object value) => value switch
    {
        DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
        string text => DateTime.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
        _ => DateTime.SpecifyKind(
            Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            DateTimeKind.Utc),
    };

    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.DbType = DbType.String;
        parameter.Value = DateTime
            .SpecifyKind(value, DateTimeKind.Utc)
            .ToString("O", CultureInfo.InvariantCulture);
    }
}

public sealed class TextDecimalTypeHandler : SqlMapper.TypeHandler<decimal>
{
    public override decimal Parse(object value) => value switch
    {
        decimal amount => amount,
        double amount => (decimal)amount,
        string text => decimal.Parse(text, CultureInfo.InvariantCulture),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    };

    public override void SetValue(IDbDataParameter parameter, decimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.DbType = DbType.Decimal;
        parameter.Value = value;
    }
}
