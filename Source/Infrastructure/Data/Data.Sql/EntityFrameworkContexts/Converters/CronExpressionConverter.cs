using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NCrontab;

namespace Data.Sql.EntityFrameworkContexts.Converters;

public sealed class CronExpressionConverter : ValueConverter<CrontabSchedule, string>
{
    public CronExpressionConverter()
        : base(
            schedule => schedule.ToString(),
            text => CrontabSchedule.Parse(text))
    {
    }
}

