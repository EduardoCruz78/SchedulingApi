namespace Scheduling.Domain.Common;

/// <summary>Category of a failure. The API layer maps each type to an HTTP status code.</summary>
public enum ErrorType
{
    /// <summary>Input is malformed (400).</summary>
    Validation,

    /// <summary>Resource does not exist or is not visible to the caller (404).</summary>
    NotFound,

    /// <summary>State conflict, e.g. overlapping slot or duplicated e-mail (409).</summary>
    Conflict,

    /// <summary>Input is well-formed but violates a business rule (422).</summary>
    BusinessRule,

    /// <summary>Caller is not authenticated or credentials are invalid (401).</summary>
    Unauthorized,

    /// <summary>Caller is authenticated but not allowed (403).</summary>
    Forbidden,
}

/// <summary>An expected failure with a stable machine-readable code.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
