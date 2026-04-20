namespace ChatApp.Domain.Services.Presence;

public interface IPresenceFanoutResolver
{
    Task<IReadOnlyCollection<Guid>> ResolveTargetsAsync(Guid userId, CancellationToken ct = default);
}
