using CsdiStockHold.Domain;

namespace CsdiStockHold.Tests.Domain;

public class CustomerTests
{
    [Fact]
    public void RegularCustomer_CanReserveUpToThreeUnitsForTenMinutes()
    {
        var customer = new Customer(CustomerType.Regular);

        Assert.Equal(CustomerType.Regular, customer.Type);
        Assert.Equal(3, customer.MaxReservableUnits);
        Assert.Equal(TimeSpan.FromMinutes(10), customer.ReservationDuration);
    }

    [Fact]
    public void SpecialCustomer_CanReserveUpToTenUnitsForThirtyMinutes()
    {
        var customer = new Customer(CustomerType.Special);

        Assert.Equal(CustomerType.Special, customer.Type);
        Assert.Equal(10, customer.MaxReservableUnits);
        Assert.Equal(TimeSpan.FromMinutes(30), customer.ReservationDuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Constructor_WithUndefinedType_ThrowsInvalidCustomerType(int type)
    {
        var exception = Assert.Throws<DomainException>(() => new Customer((CustomerType)type));

        Assert.Equal(DomainErrorCode.InvalidCustomerType, exception.Code);
    }
}
