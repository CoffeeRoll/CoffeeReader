using CoffeeReader.Application.Auth;
using CoffeeReader.Application.Books;
using CoffeeReader.Application.Library;
using CoffeeReader.Application.Reading;
using CoffeeReader.Application.Shelves;
using CoffeeReader.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeReader.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<LibraryScanState>();
        services.AddSingleton<LibraryLock>();

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<BookService>();
        services.AddScoped<ReaderService>();
        services.AddScoped<ProgressService>();
        services.AddScoped<ShelfService>();
        services.AddScoped<LibraryImporter>();
        services.AddScoped<LibraryService>();
        return services;
    }
}
