using Testcontainers.PostgreSql;
using Xunit;

namespace ChatApp.Tests.Integration.Infrastructure;

// Collection fixture — one container per test run, shared across all integration classes.
// Each class resets the DB via Respawn (see ChatAppFactory).
public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("chatapp_test")
        .WithUsername("chatapp")
        .WithPassword("chatapp")
        .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
