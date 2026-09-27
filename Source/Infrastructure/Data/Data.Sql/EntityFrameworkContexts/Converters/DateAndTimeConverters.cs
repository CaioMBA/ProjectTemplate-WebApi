using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Data.Sql.EntityFrameworkContexts.Converters;

public sealed class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyConverter()
        : base(
            dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            dateTime => DateOnly.FromDateTime(dateTime))
    {
    }
}

public sealed class NullableDateOnlyConverter : ValueConverter<DateOnly?, DateTime?>
{
    public NullableDateOnlyConverter()
        : base(
            dateOnly => dateOnly.HasValue ? dateOnly.Value.ToDateTime(TimeOnly.MinValue) : null,
            dateTime => dateTime.HasValue ? DateOnly.FromDateTime(dateTime.Value) : null)
    {
    }
}

public sealed class TimeOnlyConverter : ValueConverter<TimeOnly, TimeSpan>
{
    public TimeOnlyConverter()
        : base(
            timeOnly => timeOnly.ToTimeSpan(),
            timeSpan => TimeOnly.FromTimeSpan(timeSpan))
    {
    }
}

public sealed class NullableTimeOnlyConverter : ValueConverter<TimeOnly?, TimeSpan?>
{
    public NullableTimeOnlyConverter()
        : base(
            timeOnly => timeOnly.HasValue ? timeOnly.Value.ToTimeSpan() : null,
            timeSpan => timeSpan.HasValue ? TimeOnly.FromTimeSpan(timeSpan.Value) : null)
    {
    }
}
