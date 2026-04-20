namespace ChatApp.Api.Contracts.Messages;

public sealed record MessageDeletedResponse(Guid Id, string Scope, Guid? PersonalChatId, Guid? RoomId);
