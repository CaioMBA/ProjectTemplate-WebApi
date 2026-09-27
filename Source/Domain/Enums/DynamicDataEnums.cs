namespace Domain.Enums;

public enum FieldKind
{
    Unknown = 0,
    Text,
    Integer32,
    Integer64,
    Fixed,
    Floating,
    Flag,
    Timestamp,
    TimestampOffset,
    Date,
    Time,
    Uuid,
    Json,
    Binary,
    Document,
    Array,
}

public enum FilterOperator
{
    Eq = 0,
    Neq,
    In,
    NotIn,
    Contains,
    NotContains,
    StartsWith,
    NotStartsWith,
    EndsWith,
    NotEndsWith,
    Like,
    Gt,
    Gte,
    Lt,
    Lte,
    IsNull,
}

public enum SortDirection
{
    Ascending = 0,
    Descending,
}

public enum AggregateFunction
{
    Min = 0,
    Max,
    Sum,
    Average,
}

public enum RelationKind
{
    ManyToOne = 0,
    OneToMany,
}

public enum DatabaseExposure
{
    Exposed = 0,
    Excluded,
    Unreachable,
    NothingToExpose,
    NoDataSource,
}
