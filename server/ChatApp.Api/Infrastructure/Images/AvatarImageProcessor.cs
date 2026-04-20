using ChatApp.Domain.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ChatApp.Api.Infrastructure.Images;

public class AvatarImageProcessor : IAvatarImageProcessor
{
    public async Task EncodeAsync(Stream input, Stream output, CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(input, ct);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Crop,
            Size = new Size(256, 256)
        }));
        await image.SaveAsync(output, new WebpEncoder { Quality = 80 }, ct);
    }
}
