using Application.Dispatching;
using Domain.Interfaces.Messaging;
using Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests.Application;

public sealed class SenderTests
{
    [Fact]
    public async Task Send_InvokesTheRegisteredHandler()
    {
        var provider = BuildProvider(services =>
            services.AddScoped<IRequestHandler<PingQuery, Result<string>>, PingQueryHandler>());

        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new PingQuery("hello"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("handled:hello");
    }

    [Fact]
    public async Task Send_ThrowsWithAnActionableMessage_WhenNoHandlerIsRegistered()
    {
        var provider = BuildProvider(_ => { });
        var sender = provider.GetRequiredService<ISender>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => sender.Send(new PingQuery("hello"), CancellationToken.None));

        exception.Message.ShouldContain("PingQuery");
        exception.Message.ShouldContain("IRequestHandler");
    }

    [Fact]
    public async Task Send_RunsBehavioursInRegistrationOrder_OutermostFirst()
    {
        var log = new List<string>();

        var provider = BuildProvider(
            services =>
            {
                services.AddScoped<IRequestHandler<PingQuery, Result<string>>, PingQueryHandler>();
                services.AddScoped<IPipelineBehavior<PingQuery, Result<string>>, FirstBehavior>();
                services.AddScoped<IPipelineBehavior<PingQuery, Result<string>>, SecondBehavior>();
            },
            log);

        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new PingQuery("x"), CancellationToken.None);

        log.ShouldBe(["first:enter", "second:enter", "handler", "second:exit", "first:exit"]);
    }

    [Fact]
    public async Task Send_AllowsABehaviourToShortCircuitWithoutReachingTheHandler()
    {
        var log = new List<string>();

        var provider = BuildProvider(
            services =>
            {
                services.AddScoped<IRequestHandler<PingQuery, Result<string>>, PingQueryHandler>();
                services.AddScoped<IPipelineBehavior<PingQuery, Result<string>>, ShortCircuitBehavior>();
            },
            log);

        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new PingQuery("x"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ShortCircuit");

        log.ShouldBeEmpty();
    }

    private static ServiceProvider BuildProvider(
        Action<IServiceCollection> configure,
        List<string>? log = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISender, Sender>();

        services.AddSingleton(log ?? []);

        configure(services);

        return services.BuildServiceProvider();
    }

    private sealed record PingQuery(string Message) : IQuery<string>;

    private sealed class PingQueryHandler(List<string> log) : IQueryHandler<PingQuery, string>
    {
        public Task<Result<string>> Handle(PingQuery request, CancellationToken cancellationToken)
        {
            log.Add("handler");

            return Task.FromResult(Result.Success($"handled:{request.Message}"));
        }
    }

    private sealed class FirstBehavior(List<string> log) : IPipelineBehavior<PingQuery, Result<string>>
    {
        public async Task<Result<string>> Handle(
            PingQuery request,
            RequestHandlerDelegate<Result<string>> next,
            CancellationToken cancellationToken)
        {
            log.Add("first:enter");
            var response = await next(cancellationToken);
            log.Add("first:exit");

            return response;
        }
    }

    private sealed class SecondBehavior(List<string> log) : IPipelineBehavior<PingQuery, Result<string>>
    {
        public async Task<Result<string>> Handle(
            PingQuery request,
            RequestHandlerDelegate<Result<string>> next,
            CancellationToken cancellationToken)
        {
            log.Add("second:enter");
            var response = await next(cancellationToken);
            log.Add("second:exit");

            return response;
        }
    }

    private sealed class ShortCircuitBehavior : IPipelineBehavior<PingQuery, Result<string>>
    {
        public Task<Result<string>> Handle(
            PingQuery request,
            RequestHandlerDelegate<Result<string>> next,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<string>(
                Error.Validation("ShortCircuit", "Stopped before the handler.")));
    }
}
