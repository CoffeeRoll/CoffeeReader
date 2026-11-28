using CoffeeReader.Application.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace CoffeeReader.Api.Auth;

/// <summary>
/// Short-lived cache of each user's security stamp and role, so session checks don't hit the database on
/// every image request. Entries are dropped whenever an account changes.
/// </summary>
public sealed class SessionCache(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    public async Task<UserSession?> GetAsync(int userId, AuthService authService, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return await authService.GetSessionAsync(userId, cancellationToken);
        });

    public void Invalidate(int userId) => cache.Remove(Key(userId));

    private static string Key(int userId) => $"session:{userId}";
}
