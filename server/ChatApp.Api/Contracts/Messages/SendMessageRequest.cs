namespace ChatApp.Api.Contracts.Messages;

public sealed record SendMessageRequest(string Body, Guid? ReplyToId, List<Guid>? AttachmentIds = null);
