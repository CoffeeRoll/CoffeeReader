using CoffeeReader.Domain.Common;

namespace CoffeeReader.Domain.Entities;

public sealed class User
{
    public const int MaxUsernameLength = 64;
    public const int MaxDisplayNameLength = 128;

    private User()
    {
    }

    public User(string username, string displayName, string passwordHash, UserRole role, DateTime createdAt)
    {
        SetUsername(username);
        Rename(displayName);
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = createdAt;
        SecurityStamp = NewStamp();
    }

    public int Id { get; private set; }

    public string Username { get; private set; } = string.Empty;

    public string NormalizedUsername { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    /// <summary>
    /// Rotated whenever credentials change so that existing sessions are invalidated.
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    public bool IsAdmin => Role == UserRole.Admin;

    public void Rename(string displayName)
    {
        var trimmed = displayName.Trim();
        if (trimmed.Length == 0)
        {
            trimmed = Username;
        }

        if (trimmed.Length > MaxDisplayNameLength)
        {
            throw new DomainException($"Display name must be at most {MaxDisplayNameLength} characters.");
        }

        DisplayName = trimmed;
    }

    public void ChangeRole(UserRole role) => Role = role;

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        SecurityStamp = NewStamp();
    }

    /// <summary>
    /// Used when a stored hash is upgraded to a newer algorithm; keeps existing sessions valid.
    /// </summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void RecordLogin(DateTime when) => LastLoginAt = when;

    private void SetUsername(string username)
    {
        var trimmed = username.Trim();
        if (trimmed.Length is 0 or > MaxUsernameLength)
        {
            throw new DomainException($"Username must be between 1 and {MaxUsernameLength} characters.");
        }

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new DomainException("Username cannot contain spaces.");
        }

        Username = trimmed;
        NormalizedUsername = TextNormalization.NormalizeKey(trimmed);
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
