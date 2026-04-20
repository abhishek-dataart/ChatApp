using ChatApp.Domain.Services.Shared;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class ImageMagicBytesTests
{
    [Fact]
    public async Task Png_is_supported()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        (await ImageMagicBytes.IsSupportedImageAsync(ms)).Should().BeTrue();
    }

    [Fact]
    public async Task Random_bytes_not_supported()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        (await ImageMagicBytes.IsSupportedImageAsync(ms)).Should().BeFalse();
    }

    [Fact]
    public async Task Position_reset_after_check()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0 };
        using var ms = new MemoryStream(bytes);
        await ImageMagicBytes.IsSupportedImageAsync(ms);
        ms.Position.Should().Be(0);
    }

    [Fact]
    public void IsSupported_handles_short_header()
    {
        var header = new byte[12];
        ImageMagicBytes.IsSupported(header, 2).Should().BeFalse();
    }
}
