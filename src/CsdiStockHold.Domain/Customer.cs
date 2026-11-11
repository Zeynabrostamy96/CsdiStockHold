namespace CsdiStockHold.Domain;

public sealed class Customer
{
    public Customer(CustomerType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException(
                DomainErrorCode.InvalidCustomerType,
                $"Customer type '{type}' is not valid. Valid types are Regular and Special.");
        }

        Type = type;
    }

    public int Id { get; private set; }

    public CustomerType Type { get; private set; }

    /// <summary>
    /// Maximum number of units this customer can have reserved.
    /// </summary>
    public int MaxReservableUnits => GetReservationRules().MaxUnits;

    /// <summary>
    /// How long a reservation made by this customer stays valid.
    /// </summary>
    public TimeSpan ReservationDuration => GetReservationRules().Duration;

    // The customer-type table from the specification; the only place these values are defined.
    private (int MaxUnits, TimeSpan Duration) GetReservationRules() => Type switch
    {
        CustomerType.Regular => (3, TimeSpan.FromMinutes(10)),
        CustomerType.Special => (10, TimeSpan.FromMinutes(30)),
        _ => throw new InvalidOperationException($"Unknown customer type '{Type}'."),
    };
}
