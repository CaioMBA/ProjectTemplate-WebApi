using Domain.Connections;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Results;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public sealed class TransactionBehavior<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private static readonly string _requestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!ShouldUseTransaction(request))
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        if (request is not IDatabaseRequest databaseRequest)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "{RequestName} does not implement IDatabaseRequest, so it runs without a transaction.",
                    _requestName);
            }

            return await next(cancellationToken).ConfigureAwait(false);
        }

        var unitOfWork = serviceProvider.RequireKeyed<IUnitOfWork>(
            databaseRequest.DatabaseId,
            KeyedConnections.Databases);
        if (unitOfWork.HasActiveTransaction)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        return await unitOfWork
            .ExecuteInTransactionAsync<TResponse>(

                token => next(token),

                static response => response.IsSuccess,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool ShouldUseTransaction(TRequest request) =>
        request switch
        {
            ITransactionalRequest explicitOptIn => explicitOptIn.UseTransaction,
            ICommand => true,
            _ when IsGenericCommand() => true,
            _ => false,
        };

    private static bool IsGenericCommand() =>
        Array.Exists(
            typeof(TRequest).GetInterfaces(),
            contract => contract.IsGenericType
                        && contract.GetGenericTypeDefinition() == typeof(ICommand<>));
}
