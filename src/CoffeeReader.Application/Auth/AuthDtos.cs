using CoffeeReader.Application.Users;

namespace CoffeeReader.Application.Auth;

public sealed record AuthStatusDto(bool SetupRequired, UserDto? User);

public sealed record SetupRequest(string Username, string? DisplayName, string Password);

public sealed record LoginRequest(string Username, string Password, bool RememberMe = true);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// The result of a successful sign-in, carrying what the host needs to issue a session.
/// </summary>
public sealed record AuthenticatedUser(UserDto User, string SecurityStamp);
