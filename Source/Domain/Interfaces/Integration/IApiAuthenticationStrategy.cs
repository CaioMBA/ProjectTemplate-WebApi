using System.Net.Http.Headers;
using Domain.Enums;
using Domain.Models.Configuration;

namespace Domain.Interfaces.Integration;

public interface IApiAuthenticationStrategy
{
    ApiAuthorizationType AuthorizationType { get; }

    bool RequiresCredential { get; }

    void Apply(HttpRequestHeaders headers, ApiSettings api, string? credential);
}
