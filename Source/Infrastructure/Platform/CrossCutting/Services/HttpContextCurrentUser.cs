using System.Security.Claims;
using Domain.Interfaces.Identity;
using Microsoft.AspNetCore.Http;

namespace CrossCutting.Services;

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private IReadOnlyList<string>? CachedRoles { get; set; }

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Roles =>
        CachedRoles ??= Principal?.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public string? FindClaim(string claimType) => Principal?.FindFirstValue(claimType);
}
