using CsdiStockHold.Domain;
using CsdiStockHold.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CsdiStockHold.Api.Endpoints;

public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        var reservations = app.MapGroup("/api/reservations").WithTags("Reservations");

        reservations.MapPost("/", ReserveAsync)
            .WithSummary("Reserves units of a product for a customer.")
            .WithDescription("Send a unique Idempotency-Key header (at most 100 characters) with every new reservation. "
                + "The key is unique per customer. Sending the same request again with the same key does not create a second "
                + "reservation: the original reservation is returned with 200, even if it has expired in the meantime. "
                + "Using the same key for another product or quantity is rejected with 422. "
                + "A reservation stays valid for 10 minutes (Regular customer) or 30 minutes (Special customer); "
                + "the units of an expired reservation are available again. "
                + "A rejected request returns problem details whose code property names the violated rule.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        reservations.MapGet("/expired", GetExpiredAsync)
            .WithSummary("Lists the expired reservations.")
            .WithDescription("A reservation is expired from the moment of its expiry time on. The units of an expired reservation "
                + "are available again; the reservation itself is kept, so that a repeated request still returns it.");

        reservations.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Gets a reservation.")
            .WithDescription("IsExpired tells whether the reservation has expired at the time of the request.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Created<ReservationResponse>, Ok<ReservationResponse>>> ReserveAsync(
        CreateReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        ReservationService reservationService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The service checks every business rule and answers a repeated request with the original reservation.
        var (reservation, isDuplicate) = await reservationService.ReserveAsync(
            request.CustomerId,
            request.ProductId,
            request.Quantity,
            idempotencyKey,
            cancellationToken);

        var response = ReservationResponse.From(reservation, timeProvider.GetUtcNow());
        if (isDuplicate)
        {
            return TypedResults.Ok(response);
        }

        return TypedResults.Created($"/api/reservations/{reservation.Id}", response);
    }

    private static async Task<Ok<List<ReservationResponse>>> GetExpiredAsync(
        StockHoldDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // The opposite of the active reservations, which have ExpiresAt > now (see Reservation.IsExpired).
        var expired = await dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(expired.Select(r => ReservationResponse.From(r, now)).ToList());
    }

    private static async Task<Results<Ok<ReservationResponse>, NotFound>> GetAsync(
        Guid id,
        StockHoldDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (reservation is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ReservationResponse.From(reservation, timeProvider.GetUtcNow()));
    }
}

public sealed record CreateReservationRequest
{
    public required int CustomerId { get; init; }

    public required int ProductId { get; init; }

    public required int Quantity { get; init; }
}

public sealed record ReservationResponse(
    Guid Id,
    int CustomerId,
    int ProductId,
    int Quantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsExpired)
{
    public static ReservationResponse From(Reservation reservation, DateTimeOffset now) =>
        new(
            reservation.Id,
            reservation.CustomerId,
            reservation.ProductId,
            reservation.Quantity,
            reservation.CreatedAt,
            reservation.ExpiresAt,
            reservation.IsExpired(now));
}
