using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Domain.Interfaces.Modules;

public sealed record InfrastructureModuleContext(
    AppSettings Settings,
    ObservabilityOptions Observability);

public interface IInfrastructureModule
{
    string Name { get; }

    bool IsEnabled(InfrastructureModuleContext context);

    string Describe(InfrastructureModuleContext context);

    void Register(IServiceCollection services, InfrastructureModuleContext context);
}

public abstract class InfrastructureModuleBase : IInfrastructureModule
{
    public abstract string Name { get; }

    public bool IsEnabled(InfrastructureModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Entries(context).Any();
    }

    public string Describe(InfrastructureModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return $"{Name}({string.Join(", ", Entries(context))})";
    }

    public void Register(IServiceCollection services, InfrastructureModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsEnabled(context))
        {
            throw new InvalidOperationException(
                $"Module '{Name}' was registered with no matching entry in Settings. Check the module loader.");
        }

        RegisterModule(services, context);
    }

    protected abstract IEnumerable<string> Entries(InfrastructureModuleContext context);

    protected abstract void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context);
}
