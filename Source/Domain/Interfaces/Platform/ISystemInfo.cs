namespace Domain.Interfaces.Platform;

public interface ISystemInfo
{
    string MachineName { get; }

    string HostName { get; }

    int ProcessId { get; }

    string ProcessName { get; }

    string? ProcessPath { get; }

    TimeSpan ProcessUptime { get; }

    string OperatingSystemDescription { get; }

    string ProcessArchitecture { get; }

    string FrameworkDescription { get; }

    bool IsContainer { get; }

    bool IsWindows { get; }

    bool IsLinux { get; }

    int ProcessorCount { get; }

    string BaseDirectory { get; }

    string TempDirectory { get; }

    string? ApplicationVersion { get; }
}
