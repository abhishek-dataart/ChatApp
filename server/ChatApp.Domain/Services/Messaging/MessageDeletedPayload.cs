namespace ChatApp.Domain.Services.Messaging;

public sealed record MessageDeletedPayload(Guid Id, string Scope, Guid? PersonalChatId, Guid? RoomId);
