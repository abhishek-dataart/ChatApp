using ChatApp.Data;
using ChatApp.Data.Entities.Identity;
using ChatApp.Data.Entities.Rooms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var connString = Environment.GetEnvironmentVariable("SEEDER_CONN")
    ?? "Host=localhost;Port=5432;Database=chatapp;Username=chatapp;Password=chatapp";

var optsBuilder = new DbContextOptionsBuilder<ChatDbContext>()
    .UseNpgsql(connString)
    .UseSnakeCaseNamingConvention();

using var db = new ChatDbContext(optsBuilder.Options);
var hasher = new PasswordHasher<User>();

const string password = "Password1!";
const int totalUsers = 100;
const int totalRooms = 20;
const int largeRoomCount = 5;
const int largeRoomSize = 75; // 70+ users each
const int smallRoomSize = 10;

Console.WriteLine($"Connecting to: {connString.Replace("Password=chatapp", "Password=***")}");

var now = DateTimeOffset.UtcNow;

// ----- USERS -----
var existing = await db.Users
    .Where(u => u.UsernameNormalized.StartsWith("demo"))
    .ToListAsync();
var byName = existing.ToDictionary(u => u.UsernameNormalized);

var users = new List<User>();
for (int i = 1; i <= totalUsers; i++)
{
    var username = $"demo{i:D3}";
    if (byName.TryGetValue(username, out var u))
    {
        users.Add(u);
        continue;
    }
    var user = new User
    {
        Id = Guid.NewGuid(),
        Email = $"{username}@example.com",
        EmailNormalized = $"{username}@example.com",
        Username = username,
        UsernameNormalized = username,
        DisplayName = $"Demo User {i:D3}",
        SoundOnMessage = true,
        CreatedAt = now,
    };
    user.PasswordHash = hasher.HashPassword(user, password);
    db.Users.Add(user);
    users.Add(user);
}
await db.SaveChangesAsync();
Console.WriteLine($"Users ready: {users.Count}");

// ----- ROOMS -----
var existingRooms = await db.Rooms
    .Where(r => r.NameNormalized.StartsWith("demoroom"))
    .ToListAsync();
var byRoomName = existingRooms.ToDictionary(r => r.NameNormalized);

var rooms = new List<Room>();
for (int i = 1; i <= totalRooms; i++)
{
    var name = $"DemoRoom{i:D2}";
    var norm = name.ToLowerInvariant();
    if (byRoomName.TryGetValue(norm, out var r))
    {
        rooms.Add(r);
        continue;
    }
    var owner = users[(i - 1) % users.Count];
    var room = new Room
    {
        Id = Guid.NewGuid(),
        Name = name,
        NameNormalized = norm,
        Description = i <= largeRoomCount ? $"Large demo room #{i} (70+ members)" : $"Demo room #{i}",
        Visibility = RoomVisibility.Public,
        OwnerId = owner.Id,
        Capacity = 1000,
        CreatedAt = now,
    };
    db.Rooms.Add(room);
    rooms.Add(room);
}
await db.SaveChangesAsync();
Console.WriteLine($"Rooms ready: {rooms.Count}");

// ----- MEMBERSHIPS -----
var existingMem = await db.RoomMembers
    .Where(m => rooms.Select(r => r.Id).Contains(m.RoomId))
    .ToListAsync();
var memSet = new HashSet<(Guid, Guid)>(existingMem.Select(m => (m.RoomId, m.UserId)));

int memAdded = 0;
for (int i = 0; i < rooms.Count; i++)
{
    var room = rooms[i];
    int size = i < largeRoomCount ? largeRoomSize : smallRoomSize;

    // Pick users in rotating sequence so each room has different cohort.
    var memberUsers = new List<User>();
    for (int j = 0; j < size; j++)
    {
        memberUsers.Add(users[(i * 7 + j) % users.Count]);
    }
    // Ensure owner is included.
    if (!memberUsers.Any(u => u.Id == room.OwnerId))
    {
        memberUsers[0] = users.First(u => u.Id == room.OwnerId);
    }

    foreach (var u in memberUsers.DistinctBy(u => u.Id))
    {
        if (memSet.Contains((room.Id, u.Id))) continue;
        db.RoomMembers.Add(new RoomMember
        {
            RoomId = room.Id,
            UserId = u.Id,
            Role = u.Id == room.OwnerId ? RoomRole.Owner : RoomRole.Member,
            JoinedAt = now,
        });
        memSet.Add((room.Id, u.Id));
        memAdded++;
    }
}
await db.SaveChangesAsync();
Console.WriteLine($"Memberships added: {memAdded}");

// ----- SAMPLE CREDENTIALS -----
Console.WriteLine();
Console.WriteLine("=== Sample login credentials (password is the same for all) ===");
Console.WriteLine($"Password: {password}");
Console.WriteLine();
foreach (var u in users.Take(5))
{
    Console.WriteLine($"  email: {u.Email,-32}  username: {u.Username}");
}
Console.WriteLine();
Console.WriteLine("Done.");
