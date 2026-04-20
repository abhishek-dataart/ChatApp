namespace ChatApp.Domain.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid Id { get; }
    string Username { get; }
    Guid SessionId { get; }
}
