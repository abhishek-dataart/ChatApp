namespace ChatApp.Api.Contracts.Rooms;

public sealed record AuditResponse(List<AuditEntry> Items, Guid? NextBefore);
