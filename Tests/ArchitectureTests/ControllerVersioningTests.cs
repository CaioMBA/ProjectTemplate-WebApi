using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using WebApi.Controllers;

namespace ArchitectureTests;

public sealed partial class ControllerVersioningTests
{
    private static readonly string[] _versioningAttributes =
    [
        "Asp.Versioning.ApiVersionAttribute",
        "Asp.Versioning.MapToApiVersionAttribute",
        "Asp.Versioning.ApiVersionNeutralAttribute",
    ];

    private static IEnumerable<Type> Controllers =>
        typeof(Program).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type));

    [Fact]
    public void EveryControllerLivesInAVersionFolder()
    {
        var misplaced = Controllers
            .Where(type => type.Namespace is null || !VersionFolder().IsMatch(type.Namespace))
            .Select(type => type.FullName)
            .ToList();

        Controllers.ShouldNotBeEmpty();
        misplaced.ShouldBeEmpty(
            "the API version comes from the folder: put controllers under Controllers/V<n>/ "
            + $"(namespace WebApi.Controllers.V<n> or V<n>_<m>). Misplaced: {string.Join(", ", misplaced)}");
    }

    [Fact]
    public void NoControllerDeclaresItsVersionWithAttributes()
    {
        var annotated = Controllers
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Cast<MemberInfo>()
                .Prepend(type)
                .Where(member => member.GetCustomAttributes(inherit: true)
                    .Any(attribute => _versioningAttributes.Contains(attribute.GetType().FullName)))
                .Select(member => $"{type.Name}.{member.Name}"))
            .ToList();

        annotated.ShouldBeEmpty(
            "versions come from the Controllers/V<n> folder; deprecate with "
            + $"'protected override bool Deprecated => true;'. Found attributes on: {string.Join(", ", annotated)}");
    }

    [Fact]
    public void EveryControllerDerivesFromApiControllerBase()
    {
        var outsiders = Controllers
            .Where(type => !typeof(ApiControllerBase).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        outsiders.ShouldBeEmpty(
            "ApiControllerBase carries the versioned route and the Deprecated flag. "
            + $"Not derived from it: {string.Join(", ", outsiders)}");
    }

    [GeneratedRegex(@"^WebApi\.Controllers\.V\d+(_\d+)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionFolder();
}
