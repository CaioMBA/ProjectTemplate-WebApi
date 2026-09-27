using Domain.Models.Requests;
using Domain.Models.Responses;
using Domain.Results;

namespace Domain.Interfaces.Integration;

public interface IRestApiClient
{
    Task<Result<TResponse>> SendAsync<TResponse>(
        RestApiRequestModel request,
        CancellationToken cancellationToken = default);

    Task<Result<RestApiResponseModel>> SendRawAsync(
        RestApiRequestModel request,
        CancellationToken cancellationToken = default);
}
