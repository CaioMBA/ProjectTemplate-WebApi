using Domain.Enums;

namespace Domain.Models.DynamicData;

public abstract record FilterNode
{
    public virtual int Depth => 1;
}

public sealed record AndFilter(IReadOnlyList<FilterNode> Nodes) : FilterNode
{
    public override int Depth => 1 + (Nodes.Count == 0 ? 0 : Nodes.Max(node => node.Depth));
}

public sealed record OrFilter(IReadOnlyList<FilterNode> Nodes) : FilterNode
{
    public override int Depth => 1 + (Nodes.Count == 0 ? 0 : Nodes.Max(node => node.Depth));
}

public sealed record NotFilter(FilterNode Node) : FilterNode
{
    public override int Depth => 1 + Node.Depth;
}

public sealed record ConditionFilter(string Field, FilterOperator Operator, object? Value) : FilterNode;

public sealed record SortTerm(string Field, SortDirection Direction);

public sealed record DynamicQuery(
    EntitySetModel Set,
    FilterNode? Filter,
    IReadOnlyList<SortTerm> Sort,
    int Skip,
    int Take);

public sealed record RelatedQuery(
    EntitySetModel Set,
    string Field,
    IReadOnlyList<object> Values,
    FilterNode? Filter,
    IReadOnlyList<SortTerm> Sort,
    int Skip,
    int Take);
