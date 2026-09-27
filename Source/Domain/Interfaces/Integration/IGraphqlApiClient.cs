using Domain.Models.Requests;
using Domain.Results;

namespace Domain.Interfaces.Integration;

public interface IGraphqlApiClient
{
    Task<Result<TResponse>> QueryAsync<TResponse>(
        GraphqlApiRequestModel request,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse>> MutateAsync<TResponse>(
        GraphqlApiRequestModel request,
        CancellationToken cancellationToken = default);
}
