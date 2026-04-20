namespace ChatApp.Api.Hubs;

public static class ChatGroups
{
    public static string PersonalChat(Guid id) => $"pchat:{id}";
    public static string Room(Guid id) => $"room:{id}";
}
