using System.Reflection;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace ArchitectureTests;

public sealed class ConnectionSelectionTests
{
    private static readonly Type[] _connectionBoundServices =
    [
        typeof(IRepository<,>),
        typeof(IUnitOfWork),
        typeof(IOutboxWriter),
        typeof(IOutboxMaintenance),
        typeof(ISqlDatabaseAccess),
        typeof(IDynamicDataSource),
        typeof(ISqlSyntax),
        typeof(IDocumentRepository<>),
        typeof(IDistributedCache),
        typeof(IEventBus),
    ];

    private static readonly Assembly _application = typeof(Application.Dispatching.Sender).Assembly;

    private static readonly Assembly _domain = typeof(IRequest<>).Assembly;

    [Fact]
    public void ApplicationClassesNameTheEntryOfEveryConnectionBoundDependency()
    {
        var unnamed = _application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => IsConnectionBound(parameter.ParameterType))
                .Where(parameter => parameter.GetCustomAttribute<FromKeyedServicesAttribute>() is null)
                .Select(parameter => $"{type.Name}({parameter.ParameterType.Name} {parameter.Name})"))
            .ToList();

        unnamed.ShouldBeEmpty(
            "every database, cache and broker dependency must say which Settings entry it uses with "
            + "[FromKeyedServices(<Feature>Store.<Kind>Id)]. Unnamed: " + string.Join(", ", unnamed));
    }

    [Fact]
    public void EveryCommandDeclaresTheDatabaseItWritesTo()
    {
        var commands = _domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsCommand))
            .ToList();

        commands.ShouldNotBeEmpty();

        var undeclared = commands
            .Where(type => !typeof(IDatabaseRequest).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToList();

        undeclared.ShouldBeEmpty(
            "TransactionBehavior resolves the unit of work by IDatabaseRequest.DatabaseId, so a command "
            + "without it silently runs without a transaction. Missing: " + string.Join(", ", undeclared));
    }

    private static bool IsCommand(Type contract) =>
        contract == typeof(ICommand)
        || (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static bool IsConnectionBound(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

        return _connectionBoundServices.Contains(definition);
    }
}
