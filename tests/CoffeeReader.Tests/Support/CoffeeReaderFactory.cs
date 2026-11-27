using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoffeeReader.Tests.Support;

/// <summary>
/// Hosts the real app against temporary library and data folders.
/// </summary>
public sealed class CoffeeReaderFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly TempDirectory _root = new();

    public string LibraryPath => Path.Combine(_root.Path, "books");

    public string DataPath => Path.Combine(_root.Path, "config");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Storage:LibraryPath", LibraryPath);
        builder.UseSetting("Storage:DataPath", DataPath);
        builder.UseSetting("LibraryScan:ScanOnStartup", "false");
        builder.UseSetting("LibraryScan:WatchForChanges", "false");
        builder.UseSetting("LibraryScan:IntervalMinutes", "0");
    }

    public HttpClient CreateApiClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "CoffeeReader");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _root.Dispose();
        }
    }
}

public static class HttpClientExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(CoffeeReaderFactory.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, CoffeeReaderFactory.Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, CoffeeReaderFactory.Json);

    public static async Task<HttpResponseMessage> UploadAsync(this HttpClient client, byte[] file, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        content.Add(part, "files", fileName);
        return await client.PostAsync("/api/books/upload", content);
    }
}
