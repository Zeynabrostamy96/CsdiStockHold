using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Domain;

namespace CsdiStockHold.Tests.Api;

public class ReservationResponseTests
{
    private static readonly DateTimeOffset _createdAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void From_CopiesTheReservationAndItsExpiryTime()
    {
        var reservation = new Reservation(
            new Customer(CustomerType.Regular),
            new Product("Laptop", 10),
            quantity: 2,
            idempotencyKey: "order-1",
            customerActiveReservedQuantity: 0,
            productActiveReservedQuantity: 0,
            now: _createdAt);

        var response = ReservationResponse.From(reservation, _createdAt);

        Assert.Equal(reservation.Id, response.Id);
        Assert.Equal(reservation.CustomerId, response.CustomerId);
        Assert.Equal(reservation.ProductId, response.ProductId);
        Assert.Equal(2, response.Quantity);
        Assert.Equal(_createdAt, response.CreatedAt);
        Assert.Equal(_createdAt.AddMinutes(10), response.ExpiresAt);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(599, false)]
    [InlineData(600, true)]
    [InlineData(601, true)]
    public void From_ReportsAReservationOfARegularCustomerAsExpiredFromTenMinutesOn(int secondsAfterCreation, bool expectedExpired)
    {
        var reservation = new Reservation(
            new Customer(CustomerType.Regular),
            new Product("Laptop", 10),
            quantity: 1,
            idempotencyKey: "order-1",
            customerActiveReservedQuantity: 0,
            productActiveReservedQuantity: 0,
            now: _createdAt);

        var response = ReservationResponse.From(reservation, _createdAt.AddSeconds(secondsAfterCreation));

        Assert.Equal(expectedExpired, response.IsExpired);
    }
}
