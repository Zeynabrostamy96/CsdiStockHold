using System.Net;
using System.Net.Http.Json;
using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Tests.Infrastructure;

namespace CsdiStockHold.Tests.Api;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
// The specification asks that the project keeps the correct state after the application is restarted.
// A restart is a second application (a new host with its own services) on the same database.
[Trait("Category", "Integration")]
public class RestartPersistenceIntegrationTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    private static readonly DateTimeOffset _startTime = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Data_SurvivesARestartOfTheApplication()
    {
        var time = new FixedTimeProvider(_startTime);
        int productId;
        ReservationResponse original;

        // First run of the application: register a customer and a product and reserve 2 of its 5 units.
        using (var firstRun = ApiTestSupport.CreateFactory(database, time))
        using (var client = firstRun.CreateClient())
        {
            var customer = await PostAsync<CustomerResponse>(client, "/api/customers", new { type = "Regular" });
            var product = await PostAsync<ProductResponse>(client, "/api/products", new { name = "Laptop", stockQuantity = 5 });
            productId = product.Id;

            var response = await ReserveAsync(client, customer.Id, productId, 2, "order-1");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            original = (await response.Content.ReadFromJsonAsync<ReservationResponse>(ApiTestSupport.JsonOptions))!;
        }

        // Second run: nothing is kept in memory, so everything below comes from the database.
        using var secondRun = ApiTestSupport.CreateFactory(database, time);
        using var restarted = secondRun.CreateClient();

        var reloadedProduct = await restarted.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", ApiTestSupport.JsonOptions);
        Assert.Equal(5, reloadedProduct?.StockQuantity);
        Assert.Equal(3, reloadedProduct?.AvailableQuantity);

        var reloadedReservation = await restarted.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{original.Id}", ApiTestSupport.JsonOptions);
        Assert.Equal(original, reloadedReservation);

        // The idempotency key survived too: the same request returns the original reservation.
        var repeated = await ReserveAsync(restarted, original.CustomerId, productId, 2, "order-1");
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(original.Id, (await repeated.Content.ReadFromJsonAsync<ReservationResponse>(ApiTestSupport.JsonOptions))?.Id);

        // Expiry does not depend on a timer of the first run: 10 minutes later the units are available again.
        time.Advance(TimeSpan.FromMinutes(10));
        var afterExpiry = await restarted.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", ApiTestSupport.JsonOptions);
        Assert.Equal(5, afterExpiry?.AvailableQuantity);
        var expired = await restarted.GetFromJsonAsync<List<ReservationResponse>>("/api/reservations/expired", ApiTestSupport.JsonOptions);
        Assert.Contains(expired!, r => r.Id == original.Id && r.IsExpired);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>(ApiTestSupport.JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> ReserveAsync(HttpClient client, int customerId, int productId, int quantity, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
        {
            Content = JsonContent.Create(new { customerId, productId, quantity }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return await client.SendAsync(request);
    }
}
