namespace Domain.Models.Configuration;

public sealed class SecretResolutionOptions
{
    public const string SectionName = "SecretResolution";

    public const string LiteralPrefix = "plain:";

    public long MaxFileSizeBytes { get; set; } = 64 * 1024;

    public bool TrimTrailingNewLine { get; set; } = true;

    public List<string> AllowedRoots { get; set; } = [];
}
