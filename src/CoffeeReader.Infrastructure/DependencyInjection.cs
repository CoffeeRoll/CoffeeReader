using CoffeeReader.Application.Abstractions;
using CoffeeReader.Infrastructure.Ebooks.Epub;
using CoffeeReader.Infrastructure.Ebooks.Mobi;
using CoffeeReader.Infrastructure.Ebooks.Pdf;
using CoffeeReader.Infrastructure.Imaging;
using CoffeeReader.Infrastructure.Library;
using CoffeeReader.Infrastructure.Persistence;
using CoffeeReader.Infrastructure.Security;
using CoffeeReader.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeReader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<LibraryScanOptions>(configuration.GetSection(LibraryScanOptions.SectionName));

        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var dataPath = Path.GetFullPath(storage.DataPath);
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(Path.GetFullPath(storage.LibraryPath));

        var connectionString = string.IsNullOrWhiteSpace(storage.ConnectionString)
            ? $"Data Source={Path.Combine(dataPath, "coffeereader.db")}"
            : storage.ConnectionString;
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(
            connectionString,
            sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        // Sign-in cookies must survive restarts, so the keys that protect them live on the data volume.
        services.AddDataProtection()
            .SetApplicationName("CoffeeReader")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ILibraryFileSystem, LibraryFileSystem>();
        services.AddSingleton<ICoverStore, CoverStore>();
        services.AddSingleton<IEbookParser, EpubParser>();
        services.AddSingleton<IEbookContentProvider, EpubContentProvider>();
        services.AddSingleton<IEbookParser, MobiParser>();
        services.AddSingleton<IEbookContentProvider, MobiContentProvider>();
        services.AddSingleton<IEbookParser, PdfParser>();
        services.AddSingleton<IEbookContentProvider, PdfContentProvider>();

        services.AddSingleton<LibraryScanService>();
        services.AddSingleton<ILibraryScanTrigger>(provider => provider.GetRequiredService<LibraryScanService>());
        services.AddHostedService(provider => provider.GetRequiredService<LibraryScanService>());
        return services;
    }
}
