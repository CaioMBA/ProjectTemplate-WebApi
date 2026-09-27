namespace Domain.Interfaces.Configuration;

public interface ISecretResolver
{
    string? Resolve(string? rawValue, string configurationKey, bool required);

    void ResolveGraph(object settings, string rootKey);
}
