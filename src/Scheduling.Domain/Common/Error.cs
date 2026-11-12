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
/// <param name="Code">Stable identifier clients can switch on, e.g. "Appointment.SlotTaken".</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Type">Category, mapped to an HTTP status code by the API.</param>
/// <param name="Fields">Per-field messages for validation errors, keyed by field name.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? Fields = null)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("Validation.Failed", "One or more fields are invalid.", ErrorType.Validation, fields);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
