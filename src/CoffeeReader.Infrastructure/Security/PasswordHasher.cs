using CoffeeReader.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace CoffeeReader.Infrastructure.Security;

/// <summary>
/// PBKDF2 password hashing via ASP.NET Core Identity's hasher (versioned, salted, upgradeable).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object HashSubject = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(HashSubject, password);

    public PasswordCheck Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return PasswordCheck.Failed;
        }

        try
        {
            return _inner.VerifyHashedPassword(HashSubject, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordCheck.Failed;
        }
    }
}
