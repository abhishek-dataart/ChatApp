using ChatApp.Api.Contracts.Social;

namespace ChatApp.Api.Contracts.Rooms;

public sealed record AuditEntry(
    Guid Id,
    UserSummary Actor,
    UserSummary? Target,
    string Action,
    string? Detail,
    DateTimeOffset CreatedAt);
