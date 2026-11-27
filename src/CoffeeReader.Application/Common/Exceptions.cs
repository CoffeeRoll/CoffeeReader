namespace CoffeeReader.Application.Common;

public abstract class ApplicationExceptionBase(string message) : Exception(message);

public sealed class NotFoundException(string message) : ApplicationExceptionBase(message)
{
    public static NotFoundException For(string entity, object id) => new($"{entity} '{id}' was not found.");
}

public sealed class ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : ApplicationExceptionBase(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    public static ValidationException For(string field, string message) =>
        new(message, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class ConflictException(string message) : ApplicationExceptionBase(message);

public sealed class ForbiddenException(string message) : ApplicationExceptionBase(message);

public sealed class UnauthorizedException() : ApplicationExceptionBase("You need to sign in.");
