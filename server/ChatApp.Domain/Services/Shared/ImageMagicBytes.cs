namespace ChatApp.Domain.Services.Shared;

public static class ImageMagicBytes
{
    private static readonly byte[][] Signatures =
    [
        [0x89, 0x50, 0x4E, 0x47],             // PNG
        [0xFF, 0xD8, 0xFF],                   // JPEG
        [0x47, 0x49, 0x46],                   // GIF
        [0x52, 0x49, 0x46, 0x46],             // WEBP (RIFF)
    ];

    public static async Task<bool> IsSupportedImageAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[12];
        var read = await stream.ReadAsync(header.AsMemory(0, 12), ct);
        stream.Position = 0;
        return IsSupported(header, read);
    }

    public static bool IsSupported(byte[] header, int available)
    {
        foreach (var magic in Signatures)
        {
            if (available < magic.Length)
            {
                continue;
            }
            if (header.Take(magic.Length).SequenceEqual(magic))
            {
                return true;
            }
        }
        return false;
    }
}
