using System.Text.RegularExpressions;
using Domain.Enums;
using Domain.Exceptions;
using Domain.Interfaces.Configuration;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;

namespace CrossCutting.Configuration;

public sealed partial class SecretResolver(
    IFileSystem fileSystem,
    SecretResolutionOptions options) : ISecretResolver
{
    [GeneratedRegex(
        """^(?:/|[A-Za-z]:[\\/]|\\\\|\.{1,2}[\\/])""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PathShape();

    public string? Resolve(string? rawValue, string configurationKey, bool required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return required
                ? throw new SecretResolutionException(configurationKey, "No value is configured.", null)
                : null;
        }

        if (rawValue.StartsWith(SecretResolutionOptions.LiteralPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var literal = rawValue[SecretResolutionOptions.LiteralPrefix.Length..];

            return string.IsNullOrEmpty(literal) && required
                ? throw new SecretResolutionException(
                    configurationKey,
                    "The 'plain:' prefix was used with an empty value.",
                    null)
                : literal;
        }

        return IsPathShaped(rawValue)
            ? ReadSecretFile(rawValue, configurationKey)
            : rawValue;
    }

    public void ResolveGraph(object settings, string rootKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootKey);

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        Walk(settings, rootKey, visited);
    }

    private static bool IsPathShaped(string value) =>
        !value.Contains('\n', StringComparison.Ordinal)
        && !value.Contains('\r', StringComparison.Ordinal)
        && PathShape().IsMatch(value);

    private string ReadSecretFile(string rawPath, string configurationKey)
    {
        string fullPath;

        try
        {
            fullPath = fileSystem.GetFullPath(rawPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SecretResolutionException(
                configurationKey,
                "The value looks like a file path but is not a valid one.",
                rawPath);
        }

        EnsureRootIsAllowed(fullPath, configurationKey);

        if (fileSystem.DirectoryExists(fullPath))
        {
            throw new SecretResolutionException(
                configurationKey,
                "The path points at a directory, not a secret file.",
                fullPath);
        }

        if (!fileSystem.FileExists(fullPath))
        {
            throw new SecretResolutionException(
                configurationKey,
                "The value looks like a secret file path but no such file exists.",
                fullPath);
        }

        var size = fileSystem.GetFileSize(fullPath);

        if (size > options.MaxFileSizeBytes)
        {
            throw new SecretResolutionException(
                configurationKey,
                $"The secret file is {size} bytes, which exceeds the "
                + $"{options.MaxFileSizeBytes} byte limit.",
                fullPath);
        }

        var contents = fileSystem.ReadAllText(fullPath);

        if (options.TrimTrailingNewLine)
        {
            contents = contents.TrimEnd('\r', '\n');
        }

        return string.IsNullOrWhiteSpace(contents)
            ? throw new SecretResolutionException(
                configurationKey,
                "The secret file is empty or contains only whitespace.",
                fullPath)
            : contents;
    }

    private void EnsureRootIsAllowed(string fullPath, string configurationKey)
    {
        if (options.AllowedRoots.Count == 0)
        {
            return;
        }

        foreach (var root in options.AllowedRoots)
        {
            var normalized = fileSystem.GetFullPath(root);

            if (fullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new SecretResolutionException(
            configurationKey,
            "The path is outside every configured SecretResolution:AllowedRoots entry.",
            fullPath);
    }

    private void Walk(object instance, string keyPath, HashSet<object> visited)
    {
        if (!visited.Add(instance))
        {
            return;
        }

        foreach (var property in SecretPropertyPlan.For(instance.GetType()))
        {
            var value = property.Accessor.GetValue(instance);

            switch (property.Kind)
            {
                case SecretPropertyKind.Secret:
                    var resolved = Resolve(
                        value as string,
                        $"{keyPath}:{property.Accessor.Name}",
                        property.Required);

                    property.Accessor.SetValue(instance, resolved);
                    break;

                case SecretPropertyKind.Nested when value is not null:
                    Walk(value, $"{keyPath}:{property.Accessor.Name}", visited);
                    break;

                case SecretPropertyKind.NestedCollection when value is System.Collections.IEnumerable items:
                    var index = 0;

                    foreach (var item in items)
                    {
                        if (item is not null)
                        {
                            Walk(item, $"{keyPath}:{property.Accessor.Name}:{index}", visited);
                        }

                        index++;
                    }

                    break;

                default:
                    break;
            }
        }
    }
}
