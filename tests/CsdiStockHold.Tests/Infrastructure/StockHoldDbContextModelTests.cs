using CsdiStockHold.Domain;
using CsdiStockHold.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CsdiStockHold.Tests.Infrastructure;

// These tests only inspect the EF Core model; they never open a database connection.
public class StockHoldDbContextModelTests
{
    [Fact]
    public void Model_BuildsAndMapsTheThreeDbSetsToTables()
    {
        using var context = CreateContext();

        Assert.Equal(typeof(Customer), context.Customers.EntityType.ClrType);
        Assert.Equal(typeof(Product), context.Products.EntityType.ClrType);
        Assert.Equal(typeof(Reservation), context.Reservations.EntityType.ClrType);
        Assert.Equal(3, context.Model.GetEntityTypes().Count());

        Assert.Equal("Customers", context.Customers.EntityType.GetTableName());
        Assert.Equal("Products", context.Products.EntityType.GetTableName());
        Assert.Equal("Reservations", context.Reservations.EntityType.GetTableName());
    }

    [Fact]
    public void Customer_HasIdPrimaryKeyAndRequiredType()
    {
        var customer = GetEntityType<Customer>();

        Assert.Equal("Id", Assert.Single(customer.FindPrimaryKey()!.Properties).Name);
        Assert.False(customer.FindProperty(nameof(Customer.Type))!.IsNullable);
    }

    [Fact]
    public void Product_HasIdPrimaryKeyRequiredNameOfMax200AndRequiredStock()
    {
        var product = GetEntityType<Product>();
        var name = product.FindProperty(nameof(Product.Name))!;

        Assert.Equal("Id", Assert.Single(product.FindPrimaryKey()!.Properties).Name);
        Assert.False(name.IsNullable);
        Assert.Equal(200, name.GetMaxLength());
        Assert.False(product.FindProperty(nameof(Product.StockQuantity))!.IsNullable);
    }

    [Fact]
    public void Reservation_HasGuidPrimaryKeyThatIsNotGeneratedByTheDatabase()
    {
        var reservation = GetEntityType<Reservation>();
        var id = Assert.Single(reservation.FindPrimaryKey()!.Properties);

        Assert.Equal("Id", id.Name);
        Assert.Equal(typeof(Guid), id.ClrType);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
    }

    [Fact]
    public void Reservation_HasRequiredQuantityTimesAndIdempotencyKeyOfMax100()
    {
        var reservation = GetEntityType<Reservation>();
        var idempotencyKey = reservation.FindProperty(nameof(Reservation.IdempotencyKey))!;

        Assert.False(idempotencyKey.IsNullable);
        Assert.Equal(100, idempotencyKey.GetMaxLength());
        Assert.False(reservation.FindProperty(nameof(Reservation.Quantity))!.IsNullable);
        Assert.False(reservation.FindProperty(nameof(Reservation.CreatedAt))!.IsNullable);
        Assert.False(reservation.FindProperty(nameof(Reservation.ExpiresAt))!.IsNullable);
    }

    [Fact]
    public void Reservation_HasForeignKeysToCustomerAndProductWithoutCascadeDelete()
    {
        var foreignKeys = GetEntityType<Reservation>().GetForeignKeys().ToList();

        Assert.Equal(2, foreignKeys.Count);

        var toCustomer = Assert.Single(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Customer));
        Assert.Equal(nameof(Reservation.CustomerId), Assert.Single(toCustomer.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, toCustomer.DeleteBehavior);

        var toProduct = Assert.Single(foreignKeys, fk => fk.PrincipalEntityType.ClrType == typeof(Product));
        Assert.Equal(nameof(Reservation.ProductId), Assert.Single(toProduct.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, toProduct.DeleteBehavior);
    }

    [Fact]
    public void Reservation_HasUniqueIndexOnCustomerIdAndIdempotencyKey()
    {
        var index = FindIndex(GetEntityType<Reservation>(), "CustomerId,IdempotencyKey");

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Reservation_HasIndexOnProductIdAndExpiresAtThatIncludesQuantity()
    {
        var index = FindIndex(GetEntityType<Reservation>(), "ProductId,ExpiresAt");

        Assert.False(index.IsUnique);
        Assert.Equal("Quantity", Assert.Single(index.GetIncludeProperties()!));
    }

    [Fact]
    public void Migrations_ContainInitialCreateAndMatchTheModel()
    {
        using var context = CreateContext();

        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static StockHoldDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<StockHoldDbContext>().UseSqlServer().Options);

    // The design-time model keeps every configured detail (such as included index columns).
    private static IEntityType GetEntityType<TEntity>()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        return model.FindEntityType(typeof(TEntity))!;
    }

    private static IIndex FindIndex(IEntityType entityType, string propertyNames) =>
        entityType.GetIndexes().Single(index => string.Join(",", index.Properties.Select(p => p.Name)) == propertyNames);
}
