namespace Domain.Exceptions;

public sealed class SecretResolutionException : Exception
{
    public SecretResolutionException()
    {
    }

    public SecretResolutionException(string message)
        : base(message)
    {
    }

    public SecretResolutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SecretResolutionException(string configurationKey, string reason, string? attemptedPath)
        : base(BuildMessage(configurationKey, reason, attemptedPath))
    {
        ConfigurationKey = configurationKey;
        AttemptedPath = attemptedPath;
    }

    public string? ConfigurationKey { get; }

    public string? AttemptedPath { get; }

    private static string BuildMessage(string configurationKey, string reason, string? attemptedPath)
    {
        var location = string.IsNullOrWhiteSpace(attemptedPath)
            ? string.Empty
            : $" Resolved path: '{attemptedPath}'.";

        return $"Configuration key '{configurationKey}' could not be resolved. {reason}{location} "
            + "A value that starts with '/', a drive letter, '\\\\' or './' is treated as a secret file "
            + "path and must point at a readable file. To use such a value literally, prefix it with "
            + "'plain:'.";
    }
}
