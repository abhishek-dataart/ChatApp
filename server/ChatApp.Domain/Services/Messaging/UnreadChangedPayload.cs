namespace ChatApp.Domain.Services.Messaging;

public sealed record UnreadChangedPayload(string Scope, Guid ScopeId, int UnreadCount);
