namespace ChatApp.Domain.Services.Attachments;

public static class MagicBytes
{
    public sealed record Detection(string Mime, string Extension);

    public static Detection? Detect(Stream stream)
    {
        Span<byte> buf = stackalloc byte[12];
        var read = stream.Read(buf);
        stream.Position = 0;
        if (read < 3)
        {
            return null;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8 &&
            buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47 &&
            buf[4] == 0x0D && buf[5] == 0x0A && buf[6] == 0x1A && buf[7] == 0x0A)
        {
            return new Detection("image/png", ".png");
        }

        // JPEG: FF D8 FF
        if (buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF)
        {
            return new Detection("image/jpeg", ".jpg");
        }

        // GIF: 47 49 46 38 {37|39} 61
        if (read >= 6 &&
            buf[0] == 0x47 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x38 &&
            (buf[4] == 0x37 || buf[4] == 0x39) && buf[5] == 0x61)
        {
            return new Detection("image/gif", ".gif");
        }

        // WEBP: RIFF .... WEBP
        if (read >= 12 &&
            buf[0] == 0x52 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x46 &&
            buf[8] == 0x57 && buf[9] == 0x45 && buf[10] == 0x42 && buf[11] == 0x50)
        {
            return new Detection("image/webp", ".webp");
        }

        return null;
    }
}
