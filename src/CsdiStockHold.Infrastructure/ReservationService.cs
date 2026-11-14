using System.Data;
using System.Linq.Expressions;
using CsdiStockHold.Domain;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Infrastructure;

/// <summary>
/// Creates reservations inside one database transaction, so concurrent requests for the same
/// customer or product are processed one after another.
/// </summary>
public sealed class ReservationService(StockHoldDbContext dbContext, TimeProvider timeProvider)
{
    /// <returns>
    /// The reservation, and whether it is the original reservation of a repeated request
    /// (same customer and idempotency key).
    /// </returns>
    public async Task<(Reservation Reservation, bool IsDuplicate)> ReserveAsync(
        int customerId,
        int productId,
        int quantity,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // Disposing the transaction without committing it rolls it back and releases the locks.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Rows are always locked in the same order: customer, then product. Update locks are held
        // until the transaction ends. The values are sent as SQL parameters by FromSql.
        var customer = await dbContext.Customers
            .FromSql($"SELECT * FROM [Customers] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {customerId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                DomainErrorCode.CustomerNotFound,
                $"Customer {customerId} was not found.");

        var product = await dbContext.Products
            .FromSql($"SELECT * FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {productId}")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                DomainErrorCode.ProductNotFound,
                $"Product {productId} was not found.");

        var now = timeProvider.GetUtcNow();

        var existing = await dbContext.Reservations.SingleOrDefaultAsync(
            r => r.CustomerId == customerId && r.IdempotencyKey == idempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            if (existing.ProductId != productId || existing.Quantity != quantity)
            {
                throw new DomainException(
                    DomainErrorCode.IdempotencyKeyConflict,
                    "This idempotency key was already used for a different reservation request.");
            }

            // A repeated request returns the original reservation, even if it has expired.
            return (existing, true);
        }

        var customerActiveReservedQuantity = await SumActiveQuantityAsync(
            r => r.CustomerId == customerId, now, cancellationToken);
        var productActiveReservedQuantity = await SumActiveQuantityAsync(
            r => r.ProductId == productId, now, cancellationToken);

        // All business rules (quantity, key, customer limit, stock) are checked by the domain.
        var reservation = new Reservation(
            customer,
            product,
            quantity,
            idempotencyKey,
            customerActiveReservedQuantity,
            productActiveReservedQuantity,
            now);

        dbContext.Reservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (reservation, false);
    }

    public Task<int> GetActiveReservedQuantityAsync(int productId, CancellationToken cancellationToken = default) =>
        SumActiveQuantityAsync(r => r.ProductId == productId, timeProvider.GetUtcNow(), cancellationToken);

    // A reservation is active while ExpiresAt > now, the opposite of Reservation.IsExpired(now).
    private Task<int> SumActiveQuantityAsync(
        Expression<Func<Reservation, bool>> belongsTo,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.Reservations
            .Where(belongsTo)
            .Where(r => r.ExpiresAt > now)
            .SumAsync(r => r.Quantity, cancellationToken);
}
