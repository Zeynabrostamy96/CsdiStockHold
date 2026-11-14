using System.Net;
using System.Net.Http.Json;
using CsdiStockHold.Api.Endpoints;
using CsdiStockHold.Tests.Infrastructure;

namespace CsdiStockHold.Tests.Api;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
// The same races as in ReservationConcurrencyTests, but through the HTTP API, so that the whole path
// (request, dependency injection, ReservationService, database) is exercised. All requests of a round wait
// at a start gate and are then sent at the same moment; each round uses new data, and the checks hold
// whatever the order of execution was.
[Trait("Category", "Integration")]
public sealed class ReservationConcurrencyApiIntegrationTests : IClassFixture<TestDatabase>, IDisposable
{
    private const int Rounds = 5;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private sealed record Request(int CustomerId, int ProductId, int Quantity, string IdempotencyKey);

    private sealed record Result(HttpStatusCode Status, ReservationResponse? Reservation);

    public ReservationConcurrencyApiIntegrationTests(TestDatabase database)
    {
        _factory = ApiTestSupport.CreateFactory(database, _time);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Reserve_WhenTwoCustomersRequestFourOfFiveUnitsAtTheSameTime_ExactlyOneRequestSucceeds()
    {
        // The example of the specification: 5 units are available and two customers each ask for 4 at once.
        await WarmUpAsync();

        for (var round = 1; round <= Rounds; round++)
        {
            var productId = await CreateProductAsync(5);
            var firstCustomerId = await CreateCustomerAsync("Special");
            var secondCustomerId = await CreateCustomerAsync("Special");

            var results = await SendConcurrentlyAsync(
            [
                new Request(firstCustomerId, productId, 4, "first-request"),
                new Request(secondCustomerId, productId, 4, "second-request"),
            ]);

            var summary = Summarize(results);
            Assert.True(results.Count(r => r.Status == HttpStatusCode.Created) == 1, $"Round {round}: exactly one request must succeed. Results: {summary}.");
            Assert.True(results.Count(r => r.Status == HttpStatusCode.Conflict) == 1, $"Round {round}: the other request must be refused for lack of stock. Results: {summary}.");
            var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", ApiTestSupport.JsonOptions);
            Assert.True(product?.AvailableQuantity == 1, $"Round {round}: exactly 4 of the 5 units must be reserved. Results: {summary}.");
        }
    }

    [Fact]
    public async Task Reserve_WhenTheSameRequestIsSentConcurrently_CreatesOnlyOneReservation()
    {
        const int sameRequests = 10;

        await WarmUpAsync();

        for (var round = 1; round <= Rounds; round++)
        {
            var productId = await CreateProductAsync(10);
            var customerId = await CreateCustomerAsync("Regular");
            var requests = Enumerable.Repeat(new Request(customerId, productId, 2, "same-key"), sameRequests).ToList();

            var results = await SendConcurrentlyAsync(requests);

            var summary = Summarize(results);
            Assert.True(results.Count(r => r.Status == HttpStatusCode.Created) == 1, $"Round {round}: exactly one request must create the reservation. Results: {summary}.");
            Assert.True(results.Count(r => r.Status == HttpStatusCode.OK) == sameRequests - 1, $"Round {round}: all other requests must get the original reservation. Results: {summary}.");
            Assert.True(results.Select(r => r.Reservation!.Id).Distinct().Count() == 1, $"Round {round}: all responses must refer to the same reservation. Results: {summary}.");
            var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", ApiTestSupport.JsonOptions);
            Assert.True(product?.AvailableQuantity == 8, $"Round {round}: only the 2 requested units may be reserved. Results: {summary}.");
        }
    }

    // Starts the application's services, compiles the queries and warms up the connections before the first race.
    private async Task WarmUpAsync()
    {
        var productId = await CreateProductAsync(1);
        var customerId = await CreateCustomerAsync("Regular");

        await SendConcurrentlyAsync([new Request(customerId, productId, 1, "warm-up")]);
    }

    // Sends all requests at the same moment and returns the status and the reservation of each response.
    private async Task<List<Result>> SendConcurrentlyAsync(IReadOnlyList<Request> requests)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = requests
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToList();

        var workers = requests
            .Select((request, index) => Task.Run(async () =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/reservations")
                {
                    Content = JsonContent.Create(new { customerId = request.CustomerId, productId = request.ProductId, quantity = request.Quantity }),
                };
                message.Headers.Add("Idempotency-Key", request.IdempotencyKey);

                readiness[index].SetResult();
                await startGate.Task.WaitAsync(timeout.Token);

                using var response = await _client.SendAsync(message, timeout.Token);
                var reservation = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ReservationResponse>(ApiTestSupport.JsonOptions, timeout.Token)
                    : null;

                return new Result(response.StatusCode, reservation);
            }))
            .ToList();
        var allWorkers = Task.WhenAll(workers);

        try
        {
            await Task.WhenAll(readiness.Select(line => line.Task)).WaitAsync(timeout.Token);
            startGate.SetResult();

            return (await allWorkers).ToList();
        }
        finally
        {
            // Whatever happened, release the gate and wait for every request to end, so that none is left
            // running against the database that is dropped when the tests of this class have finished.
            startGate.TrySetCanceled();
            await Task.WhenAny(allWorkers);
        }
    }

    private async Task<int> CreateProductAsync(int stockQuantity)
    {
        var response = await _client.PostAsJsonAsync("/api/products", new { name = "Laptop", stockQuantity });
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>(ApiTestSupport.JsonOptions);

        return product!.Id;
    }

    private async Task<int> CreateCustomerAsync(string type)
    {
        var response = await _client.PostAsJsonAsync("/api/customers", new { type });
        var customer = await response.Content.ReadFromJsonAsync<CustomerResponse>(ApiTestSupport.JsonOptions);

        return customer!.Id;
    }

    private static string Summarize(IEnumerable<Result> results) =>
        string.Join(", ", results.Select(r => (int)r.Status));
}
