namespace Seruni22Bakery.Domain.Common;

public class Result<T>
{
    public bool Success { get; private set; }
    public T? Data { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private Result() { }

    public static Result<T> Ok(T data) => new() { Success = true, Data = data };
    public static Result<T> Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}

public class Result
{
    public bool Success { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    private Result() { }

    public static Result Ok() => new() { Success = true };
    public static Result Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}

public static class ErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string JournalUnbalanced = "JOURNAL_UNBALANCED";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InvalidOrderStatus = "INVALID_ORDER_STATUS";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string DatabaseError = "DATABASE_ERROR";
    public const string PrinterNotConfigured = "PRINTER_NOT_CONFIGURED";
    public const string PrinterNotFound = "PRINTER_NOT_FOUND";
    public const string PrintFailed = "PRINT_FAILED";
    public const string InternalError = "INTERNAL_ERROR";
}
