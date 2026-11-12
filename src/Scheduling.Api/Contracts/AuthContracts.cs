using Scheduling.Application.Auth;

namespace Scheduling.Api.Contracts;

public sealed record RegisterRequest(string Name, string Email, string Password)
{
    public RegisterCommand ToCommand() => new(Name ?? string.Empty, Email ?? string.Empty, Password ?? string.Empty);
}

public sealed record LoginRequest(string Email, string Password)
{
    public LoginCommand ToCommand() => new(Email ?? string.Empty, Password ?? string.Empty);
}
