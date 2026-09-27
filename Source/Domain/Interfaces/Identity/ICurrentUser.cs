namespace Domain.Interfaces.Identity;

public interface ICurrentUser
{
    string? UserId { get; }

    string? UserName { get; }

    bool IsAuthenticated { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsInRole(string role);

    string? FindClaim(string claimType);
}
