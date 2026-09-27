using Domain.Interfaces.Scheduling;
using Hangfire.Console.Extensions;
using Hangfire.Console.Progress;

namespace Scheduling.Jobs;

public sealed class ConsoleJobProgress(IProgressBarFactory factory) : IJobProgress
{
    private IProgressBar? _bar;

    public void Report(double percent)
    {
        _bar ??= factory.Create();

        _bar?.SetValue(Math.Clamp(percent, 0, 100));
    }
}