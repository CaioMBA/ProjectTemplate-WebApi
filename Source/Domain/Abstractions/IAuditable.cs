namespace Domain.Abstractions;

public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }

    string? CreatedBy { get; set; }

    DateTime? UpdatedAtUtc { get; set; }

    string? UpdatedBy { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAtUtc { get; set; }

    string? DeletedBy { get; set; }
}
