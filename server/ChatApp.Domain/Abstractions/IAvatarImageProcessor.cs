namespace ChatApp.Domain.Abstractions;

public interface IAvatarImageProcessor
{
    Task EncodeAsync(Stream input, Stream output, CancellationToken ct = default);
}
