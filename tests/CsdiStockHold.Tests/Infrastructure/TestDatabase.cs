using CsdiStockHold.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Tests.Infrastructure;

/// <summary>
/// A temporary SQL Server database for integration tests, shared by the tests of one class
/// (use it as <c>IClassFixture&lt;TestDatabase&gt;</c>).
/// The server and the credentials come only from the <see cref="ConnectionStringVariable"/>
/// environment variable. The database named in that connection string is never used: a database
/// with a unique name is created, the EF Core migrations are applied to it, and it is dropped
/// again when the tests of the class have finished, also when a test failed.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    public const string ConnectionStringVariable = "ConnectionStrings__StockHold";

    private const string DatabaseNamePrefix = "CsdiStockHold_Test_";

    private string? _connectionString;

    public string DatabaseName { get; } = $"{DatabaseNamePrefix}{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        _connectionString = BuildConnectionString();

        try
        {
            await using var context = CreateContext();

            // Creates the database when it does not exist, then applies the migrations.
            await context.Database.MigrateAsync();
        }
        catch
        {
            await TryDropAsync();
            throw;
        }
    }

    public Task DisposeAsync() => DropAsync();

    /// <summary>
    /// Creates a new context on the temporary database. Every call returns a separate instance,
    /// so a test can write with one context and read the data back with another.
    /// </summary>
    public StockHoldDbContext CreateContext()
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("The test database has not been initialized yet.");
        }

        var options = new DbContextOptionsBuilder<StockHoldDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new StockHoldDbContext(options);
    }

    private string BuildConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"Integration tests need the environment variable '{ConnectionStringVariable}' " +
                "to contain a connection string for a SQL Server instance, but it is missing or empty. " +
                "Only the server and the credentials are used: the tests create and drop their own " +
                $"temporary database named '{DatabaseNamePrefix}<guid>' and never touch an existing one. " +
                "To run only the tests that need no database use: dotnet test --filter \"Category!=Integration\".");
        }

        try
        {
            // The database name is replaced, so the application database is never used.
            return new SqlConnectionStringBuilder(configured) { InitialCatalog = DatabaseName }.ConnectionString;
        }
        catch (ArgumentException)
        {
            // The inner exception is not passed on, so the value of the variable can never leak.
            throw new InvalidOperationException(
                $"The environment variable '{ConnectionStringVariable}' does not contain a valid SQL Server connection string.");
        }
    }

    private async Task DropAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        // Dropping is destructive, so the name is checked on the connection string actually in use.
        var databaseName = new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
        if (!databaseName.StartsWith(DatabaseNamePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to drop '{databaseName}': it is not a temporary test database.");
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    private async Task TryDropAsync()
    {
        try
        {
            await DropAsync();
        }
        catch
        {
            // Best effort while initialization is already failing: the original exception is
            // the one worth reporting. A leftover database can be found by its name prefix.
        }
    }
}
