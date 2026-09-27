using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Domain.Interfaces.Platform;

namespace CrossCutting.Services;

public sealed class SystemInfo(IEnvironmentAccessor environment) : ISystemInfo
{
    private const string ContainerVariable = "DOTNET_RUNNING_IN_CONTAINER";

    private const string HostNameVariable = "HOSTNAME";

    private static readonly string _resolvedProcessName = Process.GetCurrentProcess().ProcessName;

    private static readonly DateTime _processStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public string MachineName => Environment.MachineName;

    public string HostName => environment.GetVariable(HostNameVariable) ?? Environment.MachineName;

    public int ProcessId => Environment.ProcessId;

    public string ProcessName => _resolvedProcessName;

    public string? ProcessPath => Environment.ProcessPath;

    public TimeSpan ProcessUptime => DateTime.UtcNow - _processStartedAtUtc;

    public string OperatingSystemDescription => RuntimeInformation.OSDescription;

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string FrameworkDescription => RuntimeInformation.FrameworkDescription;

    public bool IsContainer =>
        string.Equals(environment.GetVariable(ContainerVariable), "true", StringComparison.OrdinalIgnoreCase);

    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public int ProcessorCount => Environment.ProcessorCount;

    public string BaseDirectory => AppContext.BaseDirectory;

    [SuppressMessage(
        "Security",
        "S5443:Use a directory that is not publicly writable",
        Justification = "This property only reports where the platform temp directory is. It " +
                        "neither creates nor writes a file, so the shared-directory race the " +
                        "rule guards against cannot occur here. Callers that write to it are " +
                        "responsible for creating a private subdirectory.")]
    public string TempDirectory => Path.GetTempPath();

    public string? ApplicationVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
}
