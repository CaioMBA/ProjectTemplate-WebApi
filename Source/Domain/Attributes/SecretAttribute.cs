namespace Domain.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute
{
    public SecretAttribute()
    {
    }

    public SecretAttribute(bool required) => Required = required;

    public bool Required { get; init; }
}
