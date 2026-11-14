using CsdiStockHold.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CsdiStockHold.Tests.Infrastructure;

// Generating the script only reads the migrations; no database is needed or contacted.
public class DatabaseScriptTests
{
    // If this fails, the script was not regenerated after a change of the migrations. Regenerate it with:
    // dotnet ef migrations script --idempotent --project src/CsdiStockHold.Infrastructure
    //     --startup-project src/CsdiStockHold.Api --output database/create-database.sql
    [Fact]
    public void CreateDatabaseScript_IsTheIdempotentScriptOfTheMigrations()
    {
        var options = new DbContextOptionsBuilder<StockHoldDbContext>().UseSqlServer().Options;
        using var context = new StockHoldDbContext(options);

        var generated = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        var committed = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "database", "create-database.sql"));

        Assert.Equal(Normalize(generated), Normalize(committed));
    }

    // The line endings and the blank lines around the script do not matter.
    private static string Normalize(string script) => script.Replace("\r\n", "\n").Trim();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CsdiStockHold.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"The repository root (the folder with CsdiStockHold.slnx) was not found above {AppContext.BaseDirectory}.");
    }
}
