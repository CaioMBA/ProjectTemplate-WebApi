namespace Domain.Interfaces.Integration;

public interface IGrpcApiClient
{
    TClient GetClient<TClient>(string apiId)
        where TClient : class;
}
