namespace CoffeeReader.Application.Common;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 256;

    public static void Validate(string? password, string field = "password")
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            throw ValidationException.For(field, $"Passwords need at least {MinLength} characters.");
        }

        if (password.Length > MaxLength)
        {
            throw ValidationException.For(field, $"Passwords can be at most {MaxLength} characters.");
        }
    }
}
