namespace CsdiStockHold.Domain;

public sealed class Reservation
{
    public const int MaxIdempotencyKeyLength = 100;

    /// <summary>
    /// Creates a reservation after checking the business rules of the specification.
    /// </summary>
    /// <param name="customerActiveReservedQuantity">Units held by the customer's active reservations, across all products.</param>
    /// <param name="productActiveReservedQuantity">Units held by the product's active reservations.</param>
    public Reservation(
        Customer customer,
        Product product,
        int quantity,
        string idempotencyKey,
        int customerActiveReservedQuantity,
        int productActiveReservedQuantity,
        DateTimeOffset now)
    {
        if (quantity < 1)
        {
            throw new DomainException(
                DomainErrorCode.InvalidQuantity,
                $"Quantity must be at least 1. Value: {quantity}.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException(
                DomainErrorCode.InvalidIdempotencyKey,
                "Idempotency key is required.");
        }

        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException(
                DomainErrorCode.InvalidIdempotencyKey,
                $"Idempotency key cannot be longer than {MaxIdempotencyKeyLength} characters.");
        }

        // Written as a subtraction so a very large quantity cannot overflow the check.
        if (quantity > customer.MaxReservableUnits - customerActiveReservedQuantity)
        {
            throw new DomainException(
                DomainErrorCode.CustomerLimitExceeded,
                $"Customer can have at most {customer.MaxReservableUnits} reserved units. " +
                $"Already reserved: {customerActiveReservedQuantity}, requested: {quantity}.");
        }

        var availableQuantity = product.GetAvailableQuantity(productActiveReservedQuantity);
        if (quantity > availableQuantity)
        {
            throw new DomainException(
                DomainErrorCode.InsufficientStock,
                $"Not enough stock. Available: {availableQuantity}, requested: {quantity}.");
        }

        Id = Guid.NewGuid();
        ProductId = product.Id;
        CustomerId = customer.Id;
        Quantity = quantity;
        IdempotencyKey = idempotencyKey;
        CreatedAt = now;
        ExpiresAt = now + customer.ReservationDuration;
    }

    // Used only by EF Core to load reservations from the database.
    private Reservation()
    {
        IdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public int ProductId { get; private set; }

    public int CustomerId { get; private set; }

    public int Quantity { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// A reservation is no longer valid once its validity period has ended, i.e. from ExpiresAt onwards.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
