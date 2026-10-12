using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tansekak.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Tansekak.Infrastructure.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>
/// One Postgres container per test run. Migrations are applied once to a template database,
/// and each test gets its own copy so tests never share rows or locks.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "tansekak_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteOnServerAsync($"CREATE DATABASE {TemplateDatabase}");

        await using (var db = CreateContext(ConnectionStringFor(TemplateDatabase)))
            await db.Database.MigrateAsync();

        NpgsqlConnection.ClearAllPools();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}");
        return ConnectionStringFor(name);
    }

    public static AppDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
