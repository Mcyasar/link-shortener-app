namespace LinkShortener.Application.Common.Results;

public record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "Null value was provided.");

    // Specific errors for RefreshTokenCommandHandler
    public static Error Unauthorized(string message) => new("Auth.Unauthorized", message);
    public static Error NotFound(string message) => new("General.NotFound", message);
    public static Error InvalidOperation(string message) => new("General.InvalidOperation", message);
}