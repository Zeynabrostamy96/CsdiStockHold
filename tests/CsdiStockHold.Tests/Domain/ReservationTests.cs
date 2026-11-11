using CsdiStockHold.Domain;

namespace CsdiStockHold.Tests.Domain;

public class ReservationTests
{
    private static DateTimeOffset Now => new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidRequest_CreatesReservation()
    {
        var customer = new Customer(CustomerType.Regular);
        var product = new Product("Laptop", 10);

        var reservation = new Reservation(customer, product, 2, "order-123", 0, 0, Now);

        Assert.NotEqual(Guid.Empty, reservation.Id);
        Assert.Equal(product.Id, reservation.ProductId);
        Assert.Equal(customer.Id, reservation.CustomerId);
        Assert.Equal(2, reservation.Quantity);
        Assert.Equal("order-123", reservation.IdempotencyKey);
        Assert.Equal(Now, reservation.CreatedAt);
    }

    [Fact]
    public void Constructor_AssignsUniqueId()
    {
        var first = CreateReservation(idempotencyKey: "key-1");
        var second = CreateReservation(idempotencyKey: "key-2");

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(CustomerType.Regular, 10)]
    [InlineData(CustomerType.Special, 30)]
    public void Constructor_SetsExpiryFromCustomerType(CustomerType customerType, int expectedMinutes)
    {
        var reservation = CreateReservation(customer: new Customer(customerType));

        Assert.Equal(Now, reservation.CreatedAt);
        Assert.Equal(Now.AddMinutes(expectedMinutes), reservation.ExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithQuantityLessThanOne_ThrowsInvalidQuantity(int quantity)
    {
        var exception = Assert.Throws<DomainException>(() => CreateReservation(quantity: quantity));

        Assert.Equal(DomainErrorCode.InvalidQuantity, exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingIdempotencyKey_ThrowsInvalidIdempotencyKey(string? idempotencyKey)
    {
        var exception = Assert.Throws<DomainException>(() => CreateReservation(idempotencyKey: idempotencyKey!));

        Assert.Equal(DomainErrorCode.InvalidIdempotencyKey, exception.Code);
    }

    [Theory]
    [InlineData(CustomerType.Regular, 0, 4)]
    [InlineData(CustomerType.Regular, 2, 2)]
    [InlineData(CustomerType.Special, 0, 11)]
    [InlineData(CustomerType.Special, 10, 1)]
    public void Constructor_WhenCustomerLimitIsExceeded_ThrowsCustomerLimitExceeded(
        CustomerType customerType,
        int customerActiveReservedQuantity,
        int quantity)
    {
        var exception = Assert.Throws<DomainException>(() => CreateReservation(
            customer: new Customer(customerType),
            product: new Product("Laptop", 100),
            quantity: quantity,
            customerActiveReservedQuantity: customerActiveReservedQuantity));

        Assert.Equal(DomainErrorCode.CustomerLimitExceeded, exception.Code);
    }

    [Theory]
    [InlineData(CustomerType.Regular, 0, 3)]
    [InlineData(CustomerType.Regular, 2, 1)]
    [InlineData(CustomerType.Special, 0, 10)]
    [InlineData(CustomerType.Special, 9, 1)]
    public void Constructor_AtCustomerLimit_CreatesReservation(
        CustomerType customerType,
        int customerActiveReservedQuantity,
        int quantity)
    {
        var reservation = CreateReservation(
            customer: new Customer(customerType),
            product: new Product("Laptop", 100),
            quantity: quantity,
            customerActiveReservedQuantity: customerActiveReservedQuantity);

        Assert.Equal(quantity, reservation.Quantity);
    }

    [Theory]
    [InlineData(5, 4, 2)]
    [InlineData(5, 5, 1)]
    [InlineData(0, 0, 1)]
    public void Constructor_WhenStockIsInsufficient_ThrowsInsufficientStock(
        int stockQuantity,
        int productActiveReservedQuantity,
        int quantity)
    {
        var exception = Assert.Throws<DomainException>(() => CreateReservation(
            customer: new Customer(CustomerType.Special),
            product: new Product("Laptop", stockQuantity),
            quantity: quantity,
            productActiveReservedQuantity: productActiveReservedQuantity));

        Assert.Equal(DomainErrorCode.InsufficientStock, exception.Code);
    }

    [Fact]
    public void Constructor_WhenRequestUsesAllAvailableStock_CreatesReservation()
    {
        var product = new Product("Laptop", 5);

        var reservation = CreateReservation(
            customer: new Customer(CustomerType.Special),
            product: product,
            quantity: 3,
            productActiveReservedQuantity: 2);

        Assert.Equal(3, reservation.Quantity);
        Assert.Equal(0, product.GetAvailableQuantity(2 + reservation.Quantity));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public void IsExpired_ForRegularCustomer_IsTrueFromTenMinutesAfterCreation(
        int secondsAfterCreation,
        bool expectedExpired)
    {
        var reservation = CreateReservation(customer: new Customer(CustomerType.Regular));

        var isExpired = reservation.IsExpired(Now.AddSeconds(secondsAfterCreation));

        Assert.Equal(expectedExpired, isExpired);
    }

    [Fact]
    public void PdfExample_FiveUnitsAvailable_FirstFourUnitRequestSucceedsAndSecondIsRejected()
    {
        var product = new Product("Laptop", 5);
        var firstCustomer = new Customer(CustomerType.Special);
        var secondCustomer = new Customer(CustomerType.Special);

        var first = new Reservation(firstCustomer, product, 4, "first-request", 0, 0, Now);
        var exception = Assert.Throws<DomainException>(
            () => new Reservation(secondCustomer, product, 4, "second-request", 0, first.Quantity, Now));

        Assert.Equal(4, first.Quantity);
        Assert.Equal(DomainErrorCode.InsufficientStock, exception.Code);
    }

    private static Reservation CreateReservation(
        Customer? customer = null,
        Product? product = null,
        int quantity = 1,
        string idempotencyKey = "key-1",
        int customerActiveReservedQuantity = 0,
        int productActiveReservedQuantity = 0) =>
        new(
            customer ?? new Customer(CustomerType.Regular),
            product ?? new Product("Laptop", 10),
            quantity,
            idempotencyKey,
            customerActiveReservedQuantity,
            productActiveReservedQuantity,
            Now);
}
