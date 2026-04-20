namespace ChatApp.Api.Contracts.Social;

public sealed record UserSearchResult(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    bool IsFriend,
    Guid? PersonalChatId);
