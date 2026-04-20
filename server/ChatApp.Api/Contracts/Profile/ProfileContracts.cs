namespace ChatApp.Api.Contracts.Profile;

public record UpdateProfileRequest(string? DisplayName, bool? SoundOnMessage);

public record ProfileResponse(Guid Id, string Email, string Username, string DisplayName, string? AvatarUrl, bool SoundOnMessage);

public record DeleteAccountRequest(string Password);
