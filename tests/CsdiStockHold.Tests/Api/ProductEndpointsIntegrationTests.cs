using System.Net;
using System.Net.Http.Json;
using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Tests.Infrastructure;

namespace CsdiStockHold.Tests.Api;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")]
public sealed class ProductEndpointsIntegrationTests : IClassFixture<TestDatabase>, IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProductEndpointsIntegrationTests(TestDatabase database)
    {
        _factory = ApiTestSupport.CreateFactory(database);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Create_WithValidRequest_Returns201WithLocationAndTheNewProduct()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new { name = "Laptop", stockQuantity = 10 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductResponse>(ApiTestSupport.JsonOptions);
        Assert.NotNull(created);
        Assert.NotEqual(0, created.Id);
        Assert.Equal("Laptop", created.Name);
        Assert.Equal(10, created.StockQuantity);
        Assert.Equal(10, created.AvailableQuantity);
        Assert.Equal($"/api/products/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_WithTheLocationOfANewProduct_ReturnsTheSameProduct()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/products", new { name = "Phone", stockQuantity = 7 });
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(ApiTestSupport.JsonOptions);

        var response = await _client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loaded = await response.Content.ReadFromJsonAsync<ProductResponse>(ApiTestSupport.JsonOptions);
        Assert.Equal(created, loaded);
    }

    [Fact]
    public async Task Get_WithAnUnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/products/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
