using System.Collections.Concurrent;
using Domain.Interfaces.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Dispatching;

public sealed class Sender(IServiceProvider serviceProvider) : ISender
{
    private static readonly ConcurrentDictionary<Type, object> _wrappers = new();

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        var wrapper = (IRequestHandlerWrapper<TResponse>)_wrappers.GetOrAdd(
            requestType,
            static (type, responseType) =>
            {
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(type, responseType);

                return Activator.CreateInstance(wrapperType)
                       ?? throw new InvalidOperationException(
                           $"Could not construct a dispatch wrapper for request type '{type}'.");
            },
            typeof(TResponse));

        return wrapper.Handle(request, serviceProvider, cancellationToken);
    }

    private interface IRequestHandlerWrapper<TResponse>
    {
        Task<TResponse> Handle(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken);
    }

    private sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : IRequestHandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(
            IRequest<TResponse> request,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
                          ?? throw new InvalidOperationException(
                              $"No handler is registered for request type '{typeof(TRequest)}'. " +
                              $"Expected an implementation of " +
                              $"'IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}>'. " +
                              $"Handlers are discovered by AddDispatcherSetup; check the handler is " +
                              $"public, non-abstract and lives in the Application assembly.");

            var typedRequest = (TRequest)request;

            RequestHandlerDelegate<TResponse> pipeline = token => handler.Handle(typedRequest, token);

            var behaviors = serviceProvider
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .Reverse();

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                var current = behavior;

                pipeline = token => current.Handle(typedRequest, next, token);
            }

            return pipeline(cancellationToken);
        }
    }
}
