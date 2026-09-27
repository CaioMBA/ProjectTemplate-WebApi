namespace Domain.Models.Persistence;

public sealed record SqlCrudStatements(
    string Select,
    string SelectAll,
    string Insert,
    string Update,
    string Delete,
    string DeleteById);