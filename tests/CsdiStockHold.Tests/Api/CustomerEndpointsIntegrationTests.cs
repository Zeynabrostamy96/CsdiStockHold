using System.Net;
using System.Net.Http.Json;
using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Domain;
using CsdiStockHold.Tests.Infrastructure;

namespace CsdiStockHold.Tests.Api;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
[Trait("Category", "Integration")]
public sealed class CustomerEndpointsIntegrationTests : IClassFixture<TestDatabase>, IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CustomerEndpointsIntegrationTests(TestDatabase database)
    {
        _factory = ApiTestSupport.CreateFactory(database);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData("Regular", CustomerType.Regular)]
    [InlineData("Special", CustomerType.Special)]
    public async Task Create_WithAValidType_Returns201WithLocationAndTheNewCustomer(string type, CustomerType expectedType)
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { type });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CustomerResponse>(ApiTestSupport.JsonOptions);
        Assert.NotNull(created);
        Assert.NotEqual(0, created.Id);
        Assert.Equal(expectedType, created.Type);
        Assert.Equal($"/api/customers/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Get_WithTheLocationOfANewCustomer_ReturnsTheSameCustomer()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/customers", new { type = "Special" });
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerResponse>(ApiTestSupport.JsonOptions);

        var response = await _client.GetAsync(createResponse.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var loaded = await response.Content.ReadFromJsonAsync<CustomerResponse>(ApiTestSupport.JsonOptions);
        Assert.Equal(created, loaded);
    }

    [Fact]
    public async Task Get_WithAnUnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/customers/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
