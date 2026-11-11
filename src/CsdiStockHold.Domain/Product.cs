namespace CsdiStockHold.Domain;

public sealed class Product
{
    public Product(string name, int stockQuantity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                DomainErrorCode.InvalidProductName,
                "Product name is required.");
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
