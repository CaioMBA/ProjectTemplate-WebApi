namespace Domain.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SqlTableAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Schema { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlKeyAttribute : Attribute
{
    public bool DatabaseGenerated { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlIgnoreAttribute : Attribute;
