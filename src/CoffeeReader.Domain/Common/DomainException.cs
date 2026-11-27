namespace CoffeeReader.Domain.Common;

/// <summary>
/// Raised when an operation would violate a domain invariant.
/// </summary>
public class DomainException(string message) : Exception(message);
