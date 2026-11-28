using CoffeeReader.Application.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace CoffeeReader.Infrastructure.Imaging;

/// <summary>
/// Keeps a resized JPEG of each cover in the data folder.
/// </summary>
public sealed class CoverStore(IOptions<StorageOptions> options) : ICoverStore
{
    public const int ThumbnailWidth = 600;
    private const int Quality = 85;

    private readonly string _directory = Path.Combine(Path.GetFullPath(options.Value.DataPath), "covers");

    public Task<bool> SaveAsync(int bookId, byte[] image, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var bitmap = SKBitmap.Decode(image);
            if (bitmap is null || bitmap.Width < 8 || bitmap.Height < 8)
            {
                return false;
            }

            var scale = Math.Min(1.0, ThumbnailWidth / (double)bitmap.Width);
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.White); // JPEG has no transparency
            using (var source = SKImage.FromBitmap(bitmap))
            {
                surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
            if (encoded is null)
            {
                return false;
            }

            Directory.CreateDirectory(_directory);
            var target = PathFor(bookId);
            var temporary = target + ".tmp";
            using (var output = File.Create(temporary))
            {
                encoded.SaveTo(output);
            }

            File.Move(temporary, target, overwrite: true);
            return true;
        }, cancellationToken);

    public CoverImage? Open(int bookId)
    {
        var path = PathFor(bookId);
        if (!File.Exists(path))
        {
            return null;
        }

        return new CoverImage(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            "image/jpeg",
            File.GetLastWriteTimeUtc(path));
    }

    public void Delete(int bookId)
    {
        var path = PathFor(bookId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(int bookId) => Path.Combine(_directory, $"{bookId}.jpg");
}
