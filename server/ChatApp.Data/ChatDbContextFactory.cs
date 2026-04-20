using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChatApp.Data;

public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=chatapp;Username=chatapp;Password=chatapp";

    public ChatDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ChatDbContext(options);
    }
}
