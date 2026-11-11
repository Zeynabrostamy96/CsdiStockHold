using CsdiStockHold.Domain;

namespace CsdiStockHold.Tests.Domain;

public class ProductTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Constructor_WithValidValues_CreatesProduct(int stockQuantity)
    {
        var product = new Product("Laptop", stockQuantity);

        Assert.Equal("Laptop", product.Name);
        Assert.Equal(stockQuantity, product.StockQuantity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithMissingName_ThrowsInvalidProductName(string? name)
    {
        var exception = Assert.Throws<DomainException>(() => new Product(name!, 10));

        Assert.Equal(DomainErrorCode.InvalidProductName, exception.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithNegativeStock_ThrowsInvalidStockQuantity(int stockQuantity)
    {
        var exception = Assert.Throws<DomainException>(() => new Product("Laptop", stockQuantity));

        Assert.Equal(DomainErrorCode.InvalidStockQuantity, exception.Code);
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 10, 0)]
    public void GetAvailableQuantity_SubtractsActiveReservedQuantity(
        int stockQuantity,
        int activeReservedQuantity,
        int expectedAvailableQuantity)
    {
        var product = new Product("Laptop", stockQuantity);

        var availableQuantity = product.GetAvailableQuantity(activeReservedQuantity);

        Assert.Equal(expectedAvailableQuantity, availableQuantity);
    }
}
