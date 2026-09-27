namespace Domain.Interfaces.Services;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }

    DateOnly TodayUtc { get; }
}
