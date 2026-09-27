namespace Domain.Enums;

public enum ErrorType
{
    None = 0,
    Validation = 1,
    Failure = 2,
    NotFound = 3,
    Conflict = 4,
    Unauthorized = 5,
    Forbidden = 6,
    Unavailable = 7,
}
