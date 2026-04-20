namespace ChatApp.Api.Contracts.Messages;

public sealed record UnreadResponse(string Scope, Guid ScopeId, int UnreadCount);
