using System.Text;
using Domain.Enums;
using Domain.Integration;
using Domain.Models.Configuration;
using Shouldly;

namespace UnitTests.Integration;

public sealed class ApiAuthenticationTests
{
    private static HttpRequestMessage Message() => new(HttpMethod.Get, "https://example.test/");

    private static ApiSettings Api(
        ApiAuthorizationType type,
        string? credential,
        string headerName = "X-Api-Key") =>
        new()
        {
            Id = "TEST",
            AuthorizationType = type,
            AuthorizationValue = credential,
            ApiKeyHeaderName = headerName,
        };

    [Fact]
    public void EveryEnumValueHasExactlyOneStrategy()
    {
        foreach (var value in Enum.GetValues<ApiAuthorizationType>())
        {
            ApiAuthentication.Resolve(value).AuthorizationType.ShouldBe(value);
        }

        ApiAuthentication.All.Select(strategy => strategy.AuthorizationType)
            .Distinct()
            .Count()
            .ShouldBe(ApiAuthentication.All.Count);
    }

    [Fact]
    public void BasicEncodesTheCredentialAsBase64()
    {
        using var message = Message();

        ApiAuthentication.Apply(Api(ApiAuthorizationType.Basic, "user:pass"), message.Headers);

        message.Headers.Authorization.ShouldNotBeNull();
        message.Headers.Authorization.Scheme.ShouldBe("Basic");
        message.Headers.Authorization.Parameter
            .ShouldBe(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass")));
    }

    [Fact]
    public void BearerPassesTheTokenThrough()
    {
        using var message = Message();

        ApiAuthentication.Apply(Api(ApiAuthorizationType.Bearer, "token-123"), message.Headers);

        message.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        message.Headers.Authorization.Parameter.ShouldBe("token-123");
    }

    [Fact]
    public void ApiKeyUsesTheConfiguredHeaderName()
    {
        using var message = Message();

        ApiAuthentication.Apply(
            Api(ApiAuthorizationType.ApiKey, "key-abc", "X-Custom-Key"),
            message.Headers);

        message.Headers.GetValues("X-Custom-Key").ShouldBe(["key-abc"]);
        message.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public void NoneLeavesTheHeadersUntouchedEvenWithoutACredential()
    {
        using var message = Message();

        ApiAuthentication.Apply(Api(ApiAuthorizationType.None, credential: null), message.Headers);

        message.Headers.Authorization.ShouldBeNull();
        message.Headers.Count().ShouldBe(0);
    }

    [Theory]
    [InlineData(ApiAuthorizationType.Basic)]
    [InlineData(ApiAuthorizationType.Bearer)]
    [InlineData(ApiAuthorizationType.ApiKey)]
    public void AMissingCredentialFailsLoudlyInsteadOfSendingAnUnauthenticatedRequest(
        ApiAuthorizationType type)
    {
        using var message = Message();

        Should.Throw<InvalidOperationException>(() =>
                ApiAuthentication.Apply(Api(type, credential: "   "), message.Headers))
            .Message.ShouldContain("AuthorizationValue is empty");
    }

    [Fact]
    public void ApiKeyWithoutAHeaderNameFailsLoudly()
    {
        using var message = Message();

        Should.Throw<InvalidOperationException>(() =>
                ApiAuthentication.Apply(
                    Api(ApiAuthorizationType.ApiKey, "key", headerName: " "),
                    message.Headers))
            .Message.ShouldContain("ApiKeyHeaderName");
    }

    [Fact]
    public void TheSameStrategyAppliesToAnHttpClientDefaultHeaders()
    {
        using var client = new HttpClient();

        ApiAuthentication.Apply(
            Api(ApiAuthorizationType.Bearer, "shared-token"),
            client.DefaultRequestHeaders);

        client.DefaultRequestHeaders.Authorization!.Parameter.ShouldBe("shared-token");
    }
}
