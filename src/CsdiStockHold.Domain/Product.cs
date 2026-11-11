namespace CsdiStockHold.Domain;

public sealed class Product
{
    public const int MaxNameLength = 200;

    public Product(string name, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                DomainErrorCode.InvalidProductName,
                "Product name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new DomainException(
                DomainErrorCode.InvalidProductName,
                $"Product name cannot be longer than {MaxNameLength} characters.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException(
                DomainErrorCode.InvalidStockQuantity,
                $"Stock quantity cannot be negative. Value: {stockQuantity}.");
        }

        Name = name;
        StockQuantity = stockQuantity;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public int StockQuantity { get; private set; }

    /// <summary>
    /// Units that can still be offered: stock minus the units held by active reservations.
    /// </summary>
    public int GetAvailableQuantity(int activeReservedQuantity) =>
        StockQuantity - activeReservedQuantity;
}
