using Domain.Interfaces.Services;

namespace CrossCutting.Services;

public sealed class SystemDateTimeProvider(TimeProvider timeProvider) : IDateTimeProvider
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly TodayUtc => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
