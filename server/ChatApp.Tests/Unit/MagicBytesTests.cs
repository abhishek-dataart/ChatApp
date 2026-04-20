using ChatApp.Domain.Services.Attachments;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class MagicBytesTests
{
    [Fact]
    public void Detects_png()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        var det = MagicBytes.Detect(ms);
        det.Should().NotBeNull();
        det!.Mime.Should().Be("image/png");
        det.Extension.Should().Be(".png");
    }

    [Fact]
    public void Detects_jpeg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        MagicBytes.Detect(ms)!.Mime.Should().Be("image/jpeg");
    }

    [Fact]
    public void Detects_gif89a()
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        MagicBytes.Detect(ms)!.Extension.Should().Be(".gif");
    }

    [Fact]
    public void Detects_webp()
    {
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
        using var ms = new MemoryStream(bytes);
        MagicBytes.Detect(ms)!.Mime.Should().Be("image/webp");
    }

    [Fact]
    public void Unknown_header_returns_null()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        MagicBytes.Detect(ms).Should().BeNull();
    }

    [Fact]
    public void Too_short_returns_null()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xD8 });
        MagicBytes.Detect(ms).Should().BeNull();
    }

    [Fact]
    public void Detect_resets_position()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        _ = MagicBytes.Detect(ms);
        ms.Position.Should().Be(0);
    }
}
