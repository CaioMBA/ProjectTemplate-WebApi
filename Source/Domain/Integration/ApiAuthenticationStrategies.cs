using System.Net.Http.Headers;
using System.Text;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;

namespace Domain.Integration;

public abstract class ApiAuthenticationStrategyBase : IApiAuthenticationStrategy
{
    public abstract ApiAuthorizationType AuthorizationType { get; }

    public virtual bool RequiresCredential => true;

    public void Apply(HttpRequestHeaders headers, ApiSettings api, string? credential)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(api);

        if (RequiresCredential && string.IsNullOrWhiteSpace(credential))
        {
            throw new InvalidOperationException(
                $"Api '{api.Id}' declares AuthorizationType '{AuthorizationType}' but "
                + "AuthorizationValue is empty. Set the credential or use AuthorizationType None.");
        }

        ApplyCredential(headers, api, credential);
    }

    protected abstract void ApplyCredential(
        HttpRequestHeaders headers,
        ApiSettings api,
        string? credential);
}

public sealed class NoneApiAuthenticationStrategy : ApiAuthenticationStrategyBase
{
    public override ApiAuthorizationType AuthorizationType => ApiAuthorizationType.None;

    public override bool RequiresCredential => false;

    protected override void ApplyCredential(
        HttpRequestHeaders headers,
        ApiSettings api,
        string? credential)
    {
    }
}

public sealed class BasicApiAuthenticationStrategy : ApiAuthenticationStrategyBase
{
    public override ApiAuthorizationType AuthorizationType => ApiAuthorizationType.Basic;

    protected override void ApplyCredential(
        HttpRequestHeaders headers,
        ApiSettings api,
        string? credential) =>
        headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(credential!)));
}

public sealed class BearerApiAuthenticationStrategy : ApiAuthenticationStrategyBase
{
    public override ApiAuthorizationType AuthorizationType => ApiAuthorizationType.Bearer;

    protected override void ApplyCredential(
        HttpRequestHeaders headers,
        ApiSettings api,
        string? credential) =>
        headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
}

public sealed class ApiKeyApiAuthenticationStrategy : ApiAuthenticationStrategyBase
{
    public override ApiAuthorizationType AuthorizationType => ApiAuthorizationType.ApiKey;

    protected override void ApplyCredential(
        HttpRequestHeaders headers,
        ApiSettings api,
        string? credential)
    {
        if (string.IsNullOrWhiteSpace(api.ApiKeyHeaderName))
        {
            throw new InvalidOperationException(
                $"Api '{api.Id}' uses ApiKey authorization but ApiKeyHeaderName is not set.");
        }

        headers.TryAddWithoutValidation(api.ApiKeyHeaderName, credential);
    }
}

public static class ApiAuthentication
{
    private static readonly IApiAuthenticationStrategy[] _strategies =
    [
        new NoneApiAuthenticationStrategy(),
        new BasicApiAuthenticationStrategy(),
        new BearerApiAuthenticationStrategy(),
        new ApiKeyApiAuthenticationStrategy(),
    ];

    public static IReadOnlyList<IApiAuthenticationStrategy> All => _strategies;

    public static IApiAuthenticationStrategy Resolve(ApiAuthorizationType authorizationType) =>
        Array.Find(_strategies, strategy => strategy.AuthorizationType == authorizationType)
        ?? throw new NotSupportedException(
            $"ApiAuthorizationType '{authorizationType}' has no IApiAuthenticationStrategy.");

    public static void Apply(ApiSettings api, HttpRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(api);

        Resolve(api.AuthorizationType).Apply(headers, api, api.AuthorizationValue);
    }
}
