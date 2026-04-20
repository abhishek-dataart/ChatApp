using ChatApp.Domain.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ChatApp.Api.Infrastructure.Images;

public class AttachmentImageProcessor : IAttachmentImageProcessor
{
    public async Task<byte[]> CreateThumbAsync(Stream source, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(source, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(512, 512),
        }));
        using var ms = new MemoryStream();
        await image.SaveAsync(ms, new JpegEncoder { Quality = 80 }, ct);
        return ms.ToArray();
    }

    public async Task<byte[]> SanitizeAsync(Stream source, string mime, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(source, ct);
        // ImageSharp preserves metadata on save by default; strip all profiles before re-encoding.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;
        using var ms = new MemoryStream();
        IImageEncoder encoder = mime.ToLowerInvariant() switch
        {
            "image/jpeg" => new JpegEncoder { Quality = 90 },
            "image/png"  => new PngEncoder { CompressionLevel = PngCompressionLevel.Level6 },
            "image/webp" => new WebpEncoder { Quality = 90 },
            "image/gif"  => new GifEncoder(),
            _ => throw new NotSupportedException($"Cannot sanitize mime '{mime}'."),
        };
        await image.SaveAsync(ms, encoder, ct);
        return ms.ToArray();
    }
}
