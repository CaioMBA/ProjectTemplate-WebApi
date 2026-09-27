using Domain.Interfaces.Configuration;
using Domain.Models.Configuration;

namespace Domain.Setup;

public static class AppSettingsPreparation
{
    public static void ResolveSecrets(AppSettings settings, ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretResolver);

        secretResolver.ResolveGraph(settings, AppSettings.SectionName);
    }

    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ConnectionErrors();
    }

    public static AppSettings Prepare(AppSettings settings, ISecretResolver secretResolver)
    {
        ResolveSecrets(settings, secretResolver);

        var errors = Validate(settings);

        return errors.Count == 0
            ? settings
            : throw new InvalidOperationException(string.Join(" ", errors));
    }
}
