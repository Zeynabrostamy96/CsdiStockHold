using CsdiStockHold.Domain;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Tests.Infrastructure;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")]
public class PersistenceTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private static readonly DateTimeOffset _creationTime = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Customer_IsSavedAndReadBackWithANewContext()
    {
        var saved = new Customer(CustomerType.Special);
        await using (var writeContext = database.CreateContext())
        {
            writeContext.Customers.Add(saved);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var loaded = await readContext.Customers.SingleAsync(c => c.Id == saved.Id);

        Assert.NotEqual(0, saved.Id);
        Assert.NotSame(saved, loaded);
        Assert.Equal(CustomerType.Special, loaded.Type);
        Assert.Equal(10, loaded.MaxReservableUnits);
        Assert.Equal(TimeSpan.FromMinutes(30), loaded.ReservationDuration);
    }

    [Fact]
    public async Task Product_IsSavedAndReadBackWithANewContext()
    {
        var saved = new Product("Laptop", 25);
        await using (var writeContext = database.CreateContext())
        {
            writeContext.Products.Add(saved);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var loaded = await readContext.Products.SingleAsync(p => p.Id == saved.Id);

        Assert.NotEqual(0, saved.Id);
        Assert.NotSame(saved, loaded);
        Assert.Equal("Laptop", loaded.Name);
        Assert.Equal(25, loaded.StockQuantity);
        Assert.Equal(20, loaded.GetAvailableQuantity(5));
    }

    [Fact]
    public async Task Reservation_IsSavedAndReadBackWithANewContext()
    {
        var saved = await SaveReservationAsync("order-1", quantity: 2);

        await using var readContext = database.CreateContext();
        var loaded = await readContext.Reservations.SingleAsync(r => r.Id == saved.Id);

        Assert.NotSame(saved, loaded);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.ProductId, loaded.ProductId);
        Assert.Equal(saved.CustomerId, loaded.CustomerId);
        Assert.Equal(2, loaded.Quantity);
        Assert.Equal("order-1", loaded.IdempotencyKey);
        Assert.Equal(_creationTime, loaded.CreatedAt);
        Assert.Equal(_creationTime.AddMinutes(10), loaded.ExpiresAt);
    }

    [Fact]
    public async Task Reservation_ReadFromTheDatabase_IsMaterializedThroughThePrivateConstructor()
    {
        var saved = await SaveReservationAsync("order-2", quantity: 1);

        await using var readContext = database.CreateContext();
        var loaded = await readContext.Reservations.SingleAsync(r => r.Id == saved.Id);

        // EF Core creates the object with the private constructor, which only gives IdempotencyKey
        // an empty placeholder, and then fills every property from the row. So nothing may still
        // hold the placeholder or a default value, and the domain behaviour must work on the result.
        Assert.Equal("order-2", loaded.IdempotencyKey);
        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.NotEqual(default, loaded.CreatedAt);
        Assert.NotEqual(default, loaded.ExpiresAt);
        Assert.False(loaded.IsExpired(_creationTime));
        Assert.False(loaded.IsExpired(_creationTime.AddMinutes(10).AddTicks(-1)));
        Assert.True(loaded.IsExpired(_creationTime.AddMinutes(10)));
    }

    // Saves a new customer and product (a reservation refers to both) and then one reservation of them.
    private async Task<Reservation> SaveReservationAsync(string idempotencyKey, int quantity)
    {
        var time = new FixedTimeProvider(_creationTime);

        await using var context = database.CreateContext();

        var customer = new Customer(CustomerType.Regular);
        var product = new Product("Laptop", 10);
        context.Customers.Add(customer);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var reservation = new Reservation(customer, product, quantity, idempotencyKey, 0, 0, time.GetUtcNow());
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();

        return reservation;
    }
}
