using System.Linq.Expressions;
using CsdiStockHold.Domain;
using CsdiStockHold.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Tests.Infrastructure;

// Integration tests: they need a SQL Server instance (see TestDatabase), so they are excluded from
// the normal run with: dotnet test --filter "Category!=Integration"
//
// Each test starts several reservation requests at the same moment and then checks what must hold
// whatever the order of execution was (never "which request won"). Every request has its own context,
// its own ReservationService and its own, already open, connection. All of them wait at a shared start
// gate, so they really compete for the same customer and product rows instead of running one by one.
[Trait("Category", "Integration")]
public class ReservationConcurrencyTests(TestDatabase database) : IClassFixture<TestDatabase>
{
    // Every test repeats its race this many times, each time with new data. A missing lock is then
    // very unlikely to go unnoticed only because the requests happened not to overlap once.
    private const int Rounds = 10;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

    // The time stands still during a race, so no reservation can expire in the middle of a test.
    private static readonly DateTimeOffset _startTime = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Request(int CustomerId, int ProductId, int Quantity, string IdempotencyKey);

    // What happened to one request: the reservation it got, or the business rule that turned it down.
    private sealed record Outcome(Reservation? Reservation, bool IsDuplicate, DomainErrorCode? Error);

    [Fact]
    public async Task ReserveAsync_WhenTwoCustomersRequestMoreThanTheStockAtTheSameTime_ExactlyOneSucceeds()
    {
        // The example of the specification: 5 units are available and two customers each ask for 4 at once.
        var time = new FixedTimeProvider(_startTime);
        await WarmUpAsync(time);

        for (var round = 1; round <= Rounds; round++)
        {
            var productId = await CreateProductAsync(5);
            var customerIds = await CreateCustomersAsync(2, CustomerType.Special);

            var outcomes = await ReserveConcurrentlyAsync(
                time,
                [
                    new Request(customerIds[0], productId, 4, "first-request"),
                    new Request(customerIds[1], productId, 4, "second-request"),
                ]);

            var stored = await GetReservationsAsync(r => r.ProductId == productId);
            var reserved = await GetActiveReservedQuantityAsync(time, productId);

            AssertStockRace(round, outcomes, stored, expectedSucceeded: 1);
            Assert.True(
                reserved <= 5,
                $"Round {round}: {reserved} units are reserved, more than the 5 in stock. Outcomes: {Summarize(outcomes)}.");
        }
    }

    [Fact]
    public async Task ReserveAsync_WhenTwentyCustomersRequestOneUnitEachForFiveUnits_ExactlyFiveSucceed()
    {
        var time = new FixedTimeProvider(_startTime);
        await WarmUpAsync(time);

        for (var round = 1; round <= Rounds; round++)
        {
            var productId = await CreateProductAsync(5);
            var customerIds = await CreateCustomersAsync(20, CustomerType.Regular);
            var requests = customerIds
                .Select((customerId, index) => new Request(customerId, productId, 1, $"request-{index}"))
                .ToList();

            var outcomes = await ReserveConcurrentlyAsync(time, requests);

            var stored = await GetReservationsAsync(r => r.ProductId == productId);
            var reserved = await GetActiveReservedQuantityAsync(time, productId);

            AssertStockRace(round, outcomes, stored, expectedSucceeded: 5);
            Assert.True(
                reserved == 5,
                $"Round {round}: exactly the 5 units in stock must be reserved, but {reserved} are. Outcomes: {Summarize(outcomes)}.");
        }
    }

    [Fact]
    public async Task ReserveAsync_WhenTheSameRequestIsSentConcurrently_CreatesOnlyOneReservation()
    {
        const int sameRequests = 10;

        var time = new FixedTimeProvider(_startTime);
        await WarmUpAsync(time);

        for (var round = 1; round <= Rounds; round++)
        {
            var productId = await CreateProductAsync(10);
            var customerId = (await CreateCustomersAsync(1, CustomerType.Regular)).Single();
            var requests = Enumerable
                .Repeat(new Request(customerId, productId, 2, "same-key"), sameRequests)
                .ToList();

            var outcomes = await ReserveConcurrentlyAsync(time, requests);

            var stored = await GetReservationsAsync(r => r.CustomerId == customerId);
            var reserved = await GetActiveReservedQuantityAsync(time, productId);
            var summary = Summarize(outcomes);

            Assert.True(
                outcomes.All(o => o.Reservation is not null),
                $"Round {round}: every repeated request must get the reservation, but some were turned down. Outcomes: {summary}.");

            var reservationIds = outcomes.Select(o => o.Reservation!.Id).Distinct().ToList();
            Assert.True(
                reservationIds.Count == 1,
                $"Round {round}: all responses must refer to the same reservation, but there were {reservationIds.Count} different ones. Outcomes: {summary}.");
            Assert.True(
                outcomes.Count(o => !o.IsDuplicate) == 1,
                $"Round {round}: exactly one request must create the reservation. Outcomes: {summary}.");
            Assert.True(
                outcomes.Count(o => o.IsDuplicate) == sameRequests - 1,
                $"Round {round}: all other requests must be reported as duplicates. Outcomes: {summary}.");
            Assert.True(
                stored.Count == 1 && stored[0].Id == reservationIds[0],
                $"Round {round}: exactly one reservation must be stored, but {stored.Count} are. Outcomes: {summary}.");
            Assert.True(
                reserved == 2,
                $"Round {round}: only the 2 requested units may be reserved, but {reserved} are. Outcomes: {summary}.");
        }
    }

    // The checks shared by both stock races: nobody gets more than the stock allows, every request that
    // did not get a reservation was turned down for lack of stock, and the stored rows are exactly the
    // reservations the successful requests received.
    private static void AssertStockRace(
        int round,
        IReadOnlyList<Outcome> outcomes,
        IReadOnlyList<Reservation> stored,
        int expectedSucceeded)
    {
        var succeeded = outcomes.Where(o => o.Reservation is not null).ToList();
        var rejectionCodes = outcomes.Where(o => o.Reservation is null).Select(o => o.Error).ToList();
        var summary = Summarize(outcomes);

        Assert.True(
            succeeded.Count == expectedSucceeded,
            $"Round {round}: exactly {expectedSucceeded} request(s) must succeed, but {succeeded.Count} did. Outcomes: {summary}.");
        Assert.True(
            rejectionCodes.All(code => code == DomainErrorCode.InsufficientStock),
            $"Round {round}: every other request must be turned down for lack of stock. Outcomes: {summary}.");
        Assert.True(
            succeeded.All(o => !o.IsDuplicate),
            $"Round {round}: no request repeats another one, so none may be a duplicate. Outcomes: {summary}.");
        Assert.True(
            stored.Count == expectedSucceeded,
            $"Round {round}: exactly {expectedSucceeded} reservation(s) must be stored, but {stored.Count} are. Outcomes: {summary}.");
        Assert.True(
            stored.Select(r => r.Id).Order().SequenceEqual(succeeded.Select(o => o.Reservation!.Id).Order()),
            $"Round {round}: the stored reservations must be exactly the ones the successful requests received. Outcomes: {summary}.");
    }

    // A compact description of what happened to each request, for the failure messages.
    private static string Summarize(IEnumerable<Outcome> outcomes) =>
        string.Join(", ", outcomes.Select(o => o.Error?.ToString() ?? (o.IsDuplicate ? "duplicate" : "created")));

    // Builds the EF Core model, compiles the queries and warms up the connection pool before the first race,
    // so that this work does not delay one request while the others are already running.
    private async Task WarmUpAsync(TimeProvider time)
    {
        var productId = await CreateProductAsync(1);
        var customerId = (await CreateCustomersAsync(1, CustomerType.Regular)).Single();

        await ReserveConcurrentlyAsync(time, [new Request(customerId, productId, 1, "warm-up")]);
    }

    // Runs all requests at the same moment and returns what happened to each of them.
    // A DomainException is an expected outcome. Any other exception (a deadlock victim, a timeout,
    // a violated unique index, ...) is a real failure and fails the test: there is deliberately no retry.
    private async Task<List<Outcome>> ReserveConcurrentlyAsync(TimeProvider time, IReadOnlyList<Request> requests)
    {
        using var timeout = new CancellationTokenSource(_timeout);
        var cancellationToken = timeout.Token;

        // Opened once every request is at the starting line.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = requests
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToList();

        async Task<Outcome> RunRequestAsync(Request request, TaskCompletionSource ready)
        {
            try
            {
                // Its own context, so its own service, transaction and connection. The connection is
                // opened now, so that opening it does not take part in the race.
                await using var context = database.CreateContext();
                await context.Database.OpenConnectionAsync(cancellationToken);
                var service = new ReservationService(context, time);

                ready.SetResult();
                await startGate.Task.WaitAsync(cancellationToken);

                try
                {
                    var (reservation, isDuplicate) = await service.ReserveAsync(
                        request.CustomerId,
                        request.ProductId,
                        request.Quantity,
                        request.IdempotencyKey,
                        cancellationToken);

                    return new Outcome(reservation, isDuplicate, null);
                }
                catch (DomainException exception)
                {
                    return new Outcome(null, false, exception.Code);
                }
            }
            catch (Exception exception)
            {
                // Tells the waiting test when the request failed before it was ready (nothing happens
                // when it was already ready), and then lets the failure through.
                ready.TrySetException(exception);
                throw;
            }
        }

        var workers = requests
            .Select((request, index) => Task.Run(() => RunRequestAsync(request, readiness[index])))
            .ToList();
        var allWorkers = Task.WhenAll(workers);

        try
        {
            await Task.WhenAll(readiness.Select(line => line.Task));
            startGate.SetResult();

            return (await allWorkers).ToList();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The concurrent requests did not finish within {_timeout.TotalSeconds:0} seconds; a lock is probably not released.");
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
        await using var context = database.CreateContext();
        var product = new Product("Laptop", stockQuantity);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<List<int>> CreateCustomersAsync(int count, CustomerType type)
    {
        await using var context = database.CreateContext();
        var customers = Enumerable.Range(0, count).Select(_ => new Customer(type)).ToList();
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();

        return customers.Select(customer => customer.Id).ToList();
    }

    // Reads with a new context, so the result comes from the database and not from a tracked entity.
    private async Task<List<Reservation>> GetReservationsAsync(Expression<Func<Reservation, bool>> where)
    {
        await using var context = database.CreateContext();

        return await context.Reservations.Where(where).ToListAsync();
    }

    private async Task<int> GetActiveReservedQuantityAsync(TimeProvider time, int productId)
    {
        await using var context = database.CreateContext();

        return await new ReservationService(context, time).GetActiveReservedQuantityAsync(productId);
    }
}
