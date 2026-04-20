namespace ChatApp.Data.Entities.Social;

public class PersonalChat
{
    public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
