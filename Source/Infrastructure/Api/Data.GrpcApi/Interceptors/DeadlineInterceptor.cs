using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Data.GrpcApi.Interceptors;

public sealed class DeadlineInterceptor(TimeSpan timeout, TimeProvider timeProvider) : Interceptor
{
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return continuation(request, WithDeadline(context));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return continuation(request, WithDeadline(context));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return continuation(request, WithDeadline(context));
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return continuation(WithDeadline(context));
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return continuation(WithDeadline(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline.HasValue || timeout <= TimeSpan.Zero)
        {
            return context;
        }

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithDeadline(timeProvider.GetUtcNow().UtcDateTime.Add(timeout)));
    }
}
