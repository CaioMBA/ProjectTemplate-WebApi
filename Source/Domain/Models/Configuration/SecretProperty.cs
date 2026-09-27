using System.Reflection;
using Domain.Enums;

namespace Domain.Models.Configuration;

public sealed record SecretProperty(PropertyInfo Accessor, SecretPropertyKind Kind, bool Required);